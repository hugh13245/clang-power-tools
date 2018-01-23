﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using ClangPowerTools.Commands;
using ClangPowerTools.DialogPages;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.CommandBars;
using EnvDTE;
using EnvDTE80;
using System.Reflection;
using System.Xml;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ClangPowerTools
{
  /// <summary>
  /// This is the class that implements the package exposed by this assembly.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The minimum requirement for a class to be considered a valid package for Visual Studio
  /// is to implement the IVsPackage interface and register itself with the shell.
  /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
  /// to do it: it derives from the Package class that provides the implementation of the
  /// IVsPackage interface and uses the registration attributes defined in the framework to
  /// register itself and its components with the shell. These attributes tell the pkgdef creation
  /// utility what data to put into .pkgdef file.
  /// </para>
  /// <para>
  /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
  /// </para>
  /// </remarks>
  [PackageRegistration(UseManagedResourcesOnly = true)]
  [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
  [ProvideMenuResource("Menus.ctmenu", 1)]
  [Guid(RunClangPowerToolsPackage.PackageGuidString)]
  [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
  [ProvideOptionPage(typeof(GeneralOptions), "Clang Power Tools", "General", 0, 0, true)]
  [ProvideOptionPage(typeof(TidyOptions), "Clang Power Tools\\Tidy", "Options", 0, 0, true, Sort = 0)]
  [ProvideOptionPage(typeof(TidyCustomChecks), "Clang Power Tools\\Tidy", "Custom Checks", 0, 0, true, Sort = 1)]
  [ProvideOptionPage(typeof(TidyChecks), "Clang Power Tools\\Tidy", "Predefined Checks", 0, 0, true, Sort = 2)]
  [ProvideOptionPage(typeof(ClangFormatPage), "Clang Power Tools", "Format", 0, 0, true, Sort = 4)]
  [ProvideAutoLoad("ADFC4E64-0397-11D1-9F4E-00A0C911004F")]
  public sealed class RunClangPowerToolsPackage : Package, IVsShellPropertyEvents, IVsSolutionEvents
  {
    #region Members

    /// <summary>
    /// RunPowerShellCommandPackage GUID string.
    /// </summary>
    public const string PackageGuidString = "f564f9d3-01ae-493e-883b-18deebdb975e";
    public static readonly Guid CommandSet = new Guid("498fdff5-5217-4da9-88d2-edad44ba3874");

    private uint mEventSinkCookie;
    //private RunningDocTableEvents mRunningDocTableEvents;

    private uint mHSolutionEvents = uint.MaxValue;
    private IVsSolution mSolution;

    #region Commands

    private CompileCommand mCompileCmd = null;
    private TidyCommand mTidyCmd = null;
    private StopClang mStopClang = null;
    private SettingsCommand mSettingsCmd = null;
    private ClangFormatCommand mClangFormatCmd = null;
    private ClangFormatPage mClangFormatPage;
    private Events mDteEvents;
    private DocumentEvents mDocumentEvents;
    private DTE2 mDte;
    private int mDocumentSavedSubscriptionCounter = 0;

    #endregion

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="TidyCommand"/> class.
    /// </summary>
    public RunClangPowerToolsPackage()
    {
      // Inside this method you can place any initialization code that does not require
      // any Visual Studio service because at this point the package object is created but
      // not sited yet inside Visual Studio environment. The place to do all the other
      // initialization is the Initialize method.
    }

    #endregion

    #region Package Members

    /// <summary>
    /// Initialization of the package; this method is called right after the package is sited, so this is the place
    /// where you can put all the initialization code that rely on services provided by VisualStudio.
    /// </summary>
    protected override void Initialize()
    {
      base.Initialize();

      mDte = GetService(typeof(DTE)) as DTE2;
      mDteEvents = mDte.Events;
      mDocumentEvents = mDteEvents.DocumentEvents;

      //Settings command is always visible
      mSettingsCmd = new SettingsCommand(this, CommandSet, CommandIds.kSettingsId);

      SubscribeToOnShellPropertyChange();
      AdviseSolutionEvents();

      mClangFormatPage = (ClangFormatPage)GetDialogPage(typeof(ClangFormatPage));
    }

    private void OptionPage_ClangFormatActivated(object aOptionPage, bool aEnabled)
    {
      SubscribeToDocumentSaved(aEnabled);
    }

    #endregion

    #region DocumentSaved event helpers

    private void SubscribeToDocumentSaved(bool aSubscribe)
    {
      if (aSubscribe)
      {
        mDocumentEvents.DocumentSaved += mClangFormatCmd.DocumentOnSave;
        ++mDocumentSavedSubscriptionCounter;
      }
      else
      {
        while (mDocumentSavedSubscriptionCounter > 0)
        {
          mDocumentEvents.DocumentSaved -= mClangFormatCmd.DocumentOnSave;
          --mDocumentSavedSubscriptionCounter;
        }
      }
    }

    #endregion

    #region IVsShellPropertyEvents Helpers

    // Subscribe to events
    private void SubscribeToOnShellPropertyChange()
    {
      if (GetService(typeof(SVsShell)) is IVsShell shellService)
        ErrorHandler.ThrowOnFailure(shellService.AdviseShellPropertyChanges(this, out mEventSinkCookie));
    }

    // Unsubscribe from events
    private void UnsubscribeFromOnShellPropertyChange()
    {
      if (GetService(typeof(SVsShell)) is IVsShell shellService)
        ErrorHandler.ThrowOnFailure(shellService.UnadviseShellPropertyChanges(mEventSinkCookie));
      mEventSinkCookie = 0;
    }

    #endregion

    #region IVsShellPropertyEvents Implementation

      
	
    public int OnShellPropertyChange(int propid, object propValue)
    {
      //Check if the toolbar was already activated
      var tidyChecks = (TidyChecks)this.GetDialogPage(typeof(TidyChecks));
      if (tidyChecks.ToolbarActivated)
      {
        UnsubscribeFromOnShellPropertyChange();
        return VSConstants.S_OK;
      }

      // Handle the event if zombie state changes from true to false
      if ((int)__VSSPROPID.VSSPROPID_Zombie != propid)
      {
        UnsubscribeFromOnShellPropertyChange();
        return VSConstants.S_OK;
      }

      if ((bool)propValue)
      {
        UnsubscribeFromOnShellPropertyChange();
        return VSConstants.S_OK;
      }

      // Show the toolbar
      var dte = GetService(typeof(DTE)) as DTE2;
      var cbs = ((CommandBars)dte.CommandBars);
      CommandBar cb = cbs["Clang Power Tools"];
      cb.Visible = true;
      tidyChecks.ToolbarActivated = true;
      tidyChecks.SaveSettingsToStorage();

      UnsubscribeFromOnShellPropertyChange();
      return VSConstants.S_OK;
    }

    #endregion

    #region Get Pointer to IVsSolutionEvents

    private void AdviseSolutionEvents()
    {
      UnadviseSolutionEvents();
      mSolution = GetService(typeof(SVsSolution)) as IVsSolution;
      mSolution?.AdviseSolutionEvents(this, out mHSolutionEvents);
    }

    private void UnadviseSolutionEvents()
    {
      if (null == mSolution)
        return;
      if (uint.MaxValue != mHSolutionEvents)
      {
        mSolution.UnadviseSolutionEvents(mHSolutionEvents);
        mHSolutionEvents = uint.MaxValue;
      }
      mSolution = null;
    }

    #endregion

    #region IVsSolutionEvents Implementation

    public int OnAfterOpenProject(IVsHierarchy aPHierarchy, int aFAdded)
    {
      return VSConstants.S_OK;
    }

    public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
    {
      return VSConstants.S_OK;
    }

    public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
    {
      return VSConstants.S_OK;
    }

    public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
    {
      return VSConstants.S_OK;
    }

    public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
    {
      return VSConstants.S_OK;
    }

    public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
    {
      return VSConstants.S_OK;
    }

    public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
    {
      if (null == mTidyCmd)
        mTidyCmd = new TidyCommand(this, CommandSet, CommandIds.kTidyId);

      if (null == mCompileCmd)
        mCompileCmd = new CompileCommand(this, CommandSet, CommandIds.kCompileId);

      if (null == mClangFormatCmd)
        mClangFormatCmd = new ClangFormatCommand(this, CommandSet, CommandIds.kClangFormat);

      if (null == mStopClang)
        mStopClang = new StopClang(this, CommandSet, CommandIds.kStopClang);

<<<<<<< HEAD
      var generalOptions = (GeneralOptions)this.GetDialogPage(typeof(GeneralOptions));
      var currentVersion = GetPackageVersion();

      if (0 != string.Compare(generalOptions.Version, currentVersion))
      {
        var dte = GetService(typeof(DTE)) as DTE2;
        OutputManager outputManager = new OutputManager(dte);
        outputManager.Show();
        outputManager.AddMessage($"🎉\tClang Power Tools was upgraded to v{currentVersion}\n" +
          $"\tCheck out what's new at http://www.clangpowertools.com/CHANGELOG");

        generalOptions.Version = currentVersion;
        generalOptions.SaveSettingsToStorage();
      }
=======
      mClangFormatPage.ClangFormatActivated += OptionPage_ClangFormatActivated;
      mClangFormatPage.LoadSettingsFromStorage();

      mDte.Events.BuildEvents.OnBuildBegin += mClangFormatCmd.OnBuildBegin;
>>>>>>> added format on save for all active documents when build event occurs

      return VSConstants.S_OK;
    }



    public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
    {
      return VSConstants.S_OK;
    }

    public int OnBeforeCloseSolution(object pUnkReserved)
    {
      SubscribeToDocumentSaved(false);

      mClangFormatPage.ClangFormatActivated -= OptionPage_ClangFormatActivated;
      return VSConstants.S_OK;
    }

    public int OnAfterCloseSolution(object pUnkReserved)
    {
      return VSConstants.S_OK;
    }

    #endregion

    #region Private Methods

    public string GetPackageVersion()
    {
      var assemblyPath = Assembly.GetExecutingAssembly().Location;
      assemblyPath = assemblyPath.Substring(0, assemblyPath.LastIndexOf('\\'));
      var manifestPath = Path.Combine(assemblyPath, "extension.vsixmanifest");

      var doc = new XmlDocument();
      doc.Load(manifestPath);
      var metaData = doc.DocumentElement.ChildNodes.Cast<XmlElement>().First(x => x.Name == "Metadata");
      var identity = metaData.ChildNodes.Cast<XmlElement>().First(x => x.Name == "Identity");

      return identity.GetAttribute("Version");
    }

    #endregion

  }
}

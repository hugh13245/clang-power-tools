﻿//------------------------------------------------------------------------------
// <copyright file="RunPowerShellCommand.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using EnvDTE80;
using System.Windows.Threading;
using System.Windows.Interop;

namespace ClangPowerTools
{
  /// <summary>
  /// Command handler
  /// </summary>
  internal sealed class TidyCommand
  {
    #region Members

    /// <summary>
    /// Command ID.
    /// </summary>
    public const int CommandId = 0x0101;

    /// <summary>
    /// Command menu group (command set GUID).
    /// </summary>
    public static readonly Guid CommandSet = new Guid("498fdff5-5217-4da9-88d2-edad44ba3874");

    /// <summary>
    /// VS Package that provides this command, not null.
    /// </summary>
    private Package mPackage;

    private DTE2 mDte;
    private string mVsEdition;
    private string mVsVersion;

    private OutputWindowManager mOutputManager;
    private ErrorsWindowManager mErrorsManager;

    #endregion

    #region Ctor

    /// <summary>
    /// Initializes a new instance of the <see cref="TidyCommand"/> class.
    /// Adds our command handlers for menu (commands must exist in the command table file)
    /// </summary>
    /// <param name="package">Owner package, not null.</param>

    private TidyCommand(Package aPackage, DTE2 aDte, string aEdition, string aVersion)
    {
      mPackage = aPackage ?? throw new ArgumentNullException("package");

      mDte = aDte;
      mVsEdition = aEdition;
      mVsVersion = aVersion;

      mOutputManager = new OutputWindowManager(mDte);
      mErrorsManager = new ErrorsWindowManager(mPackage);

      if (this.ServiceProvider.GetService(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
      {
        var menuCommandID = new CommandID(CommandSet, CommandId);
        var menuItem = new MenuCommand(this.MenuItemCallback, menuCommandID);
        commandService.AddCommand(menuItem);
      }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the instance of the command.
    /// </summary>
    public static TidyCommand Instance { get; private set; }

    /// <summary>
    /// Gets the service provider from the owner package.
    /// </summary>
    private IServiceProvider ServiceProvider => this.mPackage;

    #endregion

    #region Methods

    /// <summary>
    /// Initializes the singleton instance of the command.
    /// </summary>
    /// <param name="package">Owner package, not null.</param>
    public static void Initialize(Package aPackage, DTE2 aDte, string aEdition, string aVersion)
    {
      Instance = new TidyCommand(aPackage, aDte, aEdition, aVersion);
    }

    /// <summary>
    /// This function is the callback used to execute the command when the menu item is clicked.
    /// See the constructor to see how the menu item is associated with this function using
    /// OleMenuCommandService service and MenuCommand class.
    /// </summary>
    /// <param name="sender">Event sender.</param>
    /// <param name="e">Event args.</param>
    private void MenuItemCallback(object sender, EventArgs e)
    {
      System.Threading.Tasks.Task.Run(delegate
      {
        GeneralOptions generalOptions = (GeneralOptions)mPackage.GetDialogPage(typeof(GeneralOptions));
        TidyOptions tidyPage = (TidyOptions)mPackage.GetDialogPage(typeof(TidyOptions));

        ScriptBuiler scriptBuilder = new ScriptBuiler();
        scriptBuilder.ConstructParameters(generalOptions, tidyPage, mVsEdition, mVsVersion);

        ItemsCollector mItemsCollector = new ItemsCollector(mPackage);
        mItemsCollector.CollectSelectedFiles(mDte);

        PowerShellWrapper powerShell = new PowerShellWrapper();
        powerShell.Invoke(mItemsCollector.GetItems, scriptBuilder, mPackage);

        Dispatcher dispatcher = HwndSource.FromHwnd((IntPtr)mDte.MainWindow.HWnd).RootVisual.Dispatcher;

        dispatcher.BeginInvoke(() =>
        {
          mErrorsManager.AddErrors(powerShell.GetErrors);
          mOutputManager.AddMessages(powerShell.GetOutput);
        });
      });
    }

    #endregion

  }
}
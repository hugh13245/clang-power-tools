using ClangPowerTools.DialogPages;
using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace ClangPowerTools
{
  public abstract class ScriptBuiler
  {
    #region Members

    protected string mParameters = string.Empty;
    private bool mUseTidyFile;

    #endregion

    #region Methods

    //return the path of the folder where is located the script file or clang-format.exe
    protected virtual string GetFilePath()
    {
      string assemblyPath = Assembly.GetExecutingAssembly().Location;
      return assemblyPath.Substring(0, assemblyPath.LastIndexOf('\\'));
    }

    private string GetGeneralParameters(GeneralOptions aGeneralOptions)
    {
      string parameters = string.Empty;

      if (null != aGeneralOptions.ClangFlags && 0 < aGeneralOptions.ClangFlags.Length)
      {
        parameters = $"{parameters} {ScriptConstants.kClangFlags} (" +
          $"{string.Format("{0}", aGeneralOptions.TreatWarningsAsErrors ? string.Format("''{0}'',",ScriptConstants.kTreatWarningsAsErrors) : string.Empty)}''" +
          $"{String.Join("'',''", aGeneralOptions.ClangFlags)}'')";
      }

      if (aGeneralOptions.Continue)
        parameters = $"{parameters} {ScriptConstants.kContinue}";

      if(aGeneralOptions.VerboseMode) 
        parameters = $"{parameters} {ScriptConstants.kVerboseMode}";

      if (null != aGeneralOptions.ProjectsToIgnore && 0 < aGeneralOptions.ProjectsToIgnore.Length)
        parameters = $"{parameters} {ScriptConstants.kProjectsToIgnore} (''{String.Join("'',''", aGeneralOptions.ProjectsToIgnore)}'')";

      if (null != aGeneralOptions.FilesToIgnore && 0 < aGeneralOptions.FilesToIgnore.Length)
        parameters = $"{parameters} {ScriptConstants.kFilesToIgnore} (''{String.Join("'',''", aGeneralOptions.FilesToIgnore)}'')";

      if( 0 == string.Compare(aGeneralOptions.AdditionalIncludes, ComboBoxConstants.kSystemIncludeDirectories) )
        parameters = $"{parameters} {ScriptConstants.kSystemIncludeDirectories}";

      return $"{parameters}";
    }

    private string GetTidyParameters(TidyOptions aTidyOptions, 
      TidyChecks aTidyChecks, TidyCustomChecks aTidyCustomChecks)
    {
      string parameters = string.Empty;

      if (ComboBoxConstants.kTidyFile == aTidyOptions.UseChecksFrom)
      {
        parameters = $"{parameters}{ScriptConstants.kTidyFile}";
        mUseTidyFile = true;
      }
      else if (ComboBoxConstants.kCustomChecks == aTidyOptions.UseChecksFrom)
      {
        if(null != aTidyCustomChecks.TidyChecks && 0 != aTidyCustomChecks.TidyChecks.Length)
          parameters = $",{String.Join(",", aTidyCustomChecks.TidyChecks)}";
      }
      else if(ComboBoxConstants.kPredefinedChecks == aTidyOptions.UseChecksFrom)
      {
        foreach (PropertyInfo prop in aTidyChecks.GetType().GetProperties())
        {
          object[] propAttrs = prop.GetCustomAttributes(false);
          object clangCheckAttr = propAttrs.FirstOrDefault(attr => typeof(ClangCheckAttribute) == attr.GetType());
          object displayNameAttrObj = propAttrs.FirstOrDefault(attr => typeof(DisplayNameAttribute) == attr.GetType());

          if (null == clangCheckAttr || null == displayNameAttrObj)
            continue;

          DisplayNameAttribute displayNameAttr = (DisplayNameAttribute)displayNameAttrObj;
          var value = prop.GetValue(aTidyChecks, null);
          if (Boolean.TrueString != value.ToString())
            continue;
          parameters = $"{parameters},{displayNameAttr.DisplayName}";
        }
      }

      if (string.Empty != parameters)
      {
        parameters = string.Format("{0} ''{1}{2}''",
          (aTidyOptions.Fix ? ScriptConstants.kTidyFix : ScriptConstants.kTidy),
          (mUseTidyFile ? "" : "-*"),
          parameters);
      }

      if (!string.IsNullOrWhiteSpace(aTidyOptions.HeaderFilter))
      {
        parameters = string.Format("{0} {1} ''{2}''", parameters, ScriptConstants.kHeaderFilter,
          ComboBoxConstants.kHeaderFilterMaping.ContainsKey(aTidyOptions.HeaderFilter) ? 
          ComboBoxConstants.kHeaderFilterMaping[aTidyOptions.HeaderFilter]  : aTidyOptions.HeaderFilter);
      }

      return parameters;
    }

    #endregion

  }
}
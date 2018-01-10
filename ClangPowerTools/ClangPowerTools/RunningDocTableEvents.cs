using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Linq;

namespace ClangPowerTools
{
  public class RunningDocTableEvents : IVsRunningDocTableEvents3
  {
    #region Members

    private RunningDocumentTable mRunningDocTable;
    private DTE mDte;

    public delegate void OnBeforeSaveHander(object sender, Document document);
    public event OnBeforeSaveHander BeforeSave;

    #endregion

    #region IVsRunningDocTableEvents3 implementstion

    public RunningDocTableEvents(Package aPackage)
    {
      mRunningDocTable = new RunningDocumentTable(aPackage);
      mRunningDocTable.Advise(this);
      mDte = (DTE)Package.GetGlobalService(typeof(DTE));
    }

    public int OnAfterAttributeChange(uint aDocCookie, uint aGrfAttribs) => VSConstants.S_OK;

    public int OnAfterAttributeChangeEx(uint aDocCookie, uint aGrfAttribs, IVsHierarchy aPHierOld, uint aItemIdOld,
      string aPszMkDocumentOld, IVsHierarchy aPHierNew, uint aItemIdNew, string aPszMkDocumentNew) => VSConstants.S_OK;

    public int OnAfterDocumentWindowHide(uint aDocCookie, IVsWindowFrame aPFrame) => VSConstants.S_OK;

    public int OnAfterFirstDocumentLock(uint aDocCookie, uint aDwRDTLockType, uint aDwReadLocksRemaining,
      uint aDwEditLocksRemaining) => VSConstants.S_OK;

    public int OnAfterSave(uint aDocCookie) => VSConstants.S_OK;

    public int OnBeforeDocumentWindowShow(uint aDocCookie, int aFFirstShow, IVsWindowFrame aPFrame) => VSConstants.S_OK;

    public int OnBeforeLastDocumentUnlock(uint aDocCookie, uint aDwRDTLockType, uint aDwReadLocksRemaining, uint aDwEditLocksRemaining) => VSConstants.S_OK;

    public int OnBeforeSave(uint aDocCookie)
    {
      if (null != BeforeSave)
      {
        var document = FindDocumentByCookie(aDocCookie);
        if (null != document)
          BeforeSave(this, FindDocumentByCookie(aDocCookie));
      }
      return VSConstants.S_OK;
    }

    private Document FindDocumentByCookie(uint aDocCookie)
    {
      var documentInfo = mRunningDocTable.GetDocumentInfo(aDocCookie);
      return mDte.Documents
        .Cast<Document>()
        .FirstOrDefault(doc => doc.FullName == documentInfo.Moniker);
    }

    #endregion

  }
}

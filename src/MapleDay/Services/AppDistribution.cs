using Windows.ApplicationModel;

namespace MapleDay.Services;

public static class AppDistribution
{
    public static string Current
    {
        get
        {
            if (!WindowsStartup.IsPackaged) return "github";
            try { return Classify(true, Package.Current.SignatureKind == PackageSignatureKind.Store); }
            catch (InvalidOperationException) { return "msix"; }
        }
    }

    public static string Classify(bool packaged, bool storeSigned) =>
        !packaged ? "github" : storeSigned ? "store" : "msix";
}

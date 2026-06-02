using System.Runtime.InteropServices;
using System.Text;

namespace Optimizer.Helpers
{
    /// <summary>Detects whether the app is running as an MSIX-packaged process.</summary>
    public static class PackageInfo
    {
        private const int AppModelErrorNoPackage = 15700;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);

        public static bool IsPackaged()
        {
            try
            {
                var length = 0;
                var rc = GetCurrentPackageFullName(ref length, null);
                // APPMODEL_ERROR_NO_PACKAGE => unpackaged; anything else (e.g. ERROR_INSUFFICIENT_BUFFER) => packaged.
                return rc != AppModelErrorNoPackage;
            }
            catch
            {
                return false;
            }
        }
    }
}

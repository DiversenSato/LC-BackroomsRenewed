using System.Runtime.InteropServices;

[assembly: AssemblyVersion(VELDDev.BackroomsRenewed.PluginInfo.Version)]
[assembly: AssemblyFileVersion(VELDDev.BackroomsRenewed.PluginInfo.Version)]
[assembly: AssemblyInformationalVersion(VELDDev.BackroomsRenewed.PluginInfo.Version)]
[assembly: AssemblyCompany(VELDDev.BackroomsRenewed.PluginInfo.Author)]
[assembly: AssemblyProduct("B#CKR##MS R#N#W#D")]
[assembly: AssemblyCopyright("GPL-3.0 License")]
[assembly: AssemblyTitle(VELDDev.BackroomsRenewed.PluginInfo.PluginName)]
[assembly: AssemblyCulture("en-GB")]

namespace VELDDev.BackroomsRenewed;

public static class PluginInfo
{
    public const string PluginName = "Backrooms Renewed Fixed";
    public const string GUID = "DiversenSato.BackroomsRenewedFixed";
    public const string Author = "DiversenSato (fork of VELD-Dev's Backrooms Renewed)";

    /// <summary>GUID of the original mod this fork is based on; both can't run at once.</summary>
    public const string OriginalGUID = "VELDDev.BackroomsRenewed";
    public const string Version = "0.1.6";
}

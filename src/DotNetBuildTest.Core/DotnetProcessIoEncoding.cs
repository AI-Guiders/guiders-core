using System.Diagnostics;
using System.Text;

namespace DotNetBuildTest.Core;

/// <summary>Кодировка потоков дочернего процесса <c>dotnet</c> (Windows: без CP866/OEM в JSON).</summary>
public static class DotnetProcessIoEncoding
{
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static void ApplyUtf8(ProcessStartInfo psi)
    {
        ArgumentNullException.ThrowIfNull(psi);
        psi.StandardOutputEncoding = Utf8NoBom;
        psi.StandardErrorEncoding = Utf8NoBom;
    }
}

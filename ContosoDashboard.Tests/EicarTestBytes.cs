using System.Text;

namespace ContosoDashboard.Tests;

/// <summary>
/// Builds the 68-byte EICAR test string at runtime from two halves, so the signature never appears as one
/// contiguous literal in source or in the compiled assembly (research R-08). The bytes exist only in memory
/// and are never written to disk.
/// </summary>
public static class EicarTestBytes
{
    private const string FirstHalf = "X5O!P%@AP[4\\PZX54(P^)7CC)7}";
    private const string SecondHalf = "$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    /// <summary>The 68-byte signature.</summary>
    public static byte[] Canonical()
    {
        return Encoding.ASCII.GetBytes(string.Concat(FirstHalf, SecondHalf));
    }

    /// <summary>The signature preceded by the line "Training malware test" and CRLF.</summary>
    public static byte[] WithLeadingLine()
    {
        var prefix = Encoding.ASCII.GetBytes("Training malware test\r\n");
        var signature = Canonical();
        var combined = new byte[prefix.Length + signature.Length];
        Buffer.BlockCopy(prefix, 0, combined, 0, prefix.Length);
        Buffer.BlockCopy(signature, 0, combined, prefix.Length, signature.Length);
        return combined;
    }
}

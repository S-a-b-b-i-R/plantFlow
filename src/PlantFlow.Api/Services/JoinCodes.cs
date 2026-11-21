using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Services;

// A company's join code: 8 characters from an alphabet without look-alikes (no 0/O, 1/I/L), shown as
// "7KQM-4PXR". 31^8 is about 850 billion codes, and wrong guesses are rate-limited in JoinController.
public class JoinCodes(PlantFlowDbContext db)
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 8;

    public static string Generate() =>
        string.Create(Length, 0, (chars, _) =>
        {
            for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });

    // Accepts what people type: lower case, spaces and the hyphen are all fine.
    public static string Normalize(string input) =>
        new(input.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static string Format(string code) => code.Length == Length ? $"{code[..4]}-{code[4..]}" : code;

    // Gives the company a fresh code, replacing any old one. Retries in the very rare case of a clash.
    public async Task<string> RenewAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        string code;
        do
        {
            code = Generate();
        } while (await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.JoinCode == code, cancellationToken));

        tenant.JoinCode = code;
        tenant.JoinCodeRenewedUtc = DateTime.UtcNow;
        return code;
    }
}

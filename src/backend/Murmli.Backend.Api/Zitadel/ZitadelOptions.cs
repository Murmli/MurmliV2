using System.ComponentModel.DataAnnotations;

namespace Murmli.Backend.Api.Zitadel;

public sealed class ZitadelOptions
{
    public const string SectionName = "Zitadel";

    [Required]
    public Uri? Authority { get; set; }

    [Required]
    public string? ServiceUserToken { get; set; }
}

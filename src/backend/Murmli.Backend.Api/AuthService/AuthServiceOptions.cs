using System.ComponentModel.DataAnnotations;

namespace Murmli.Backend.Api.AuthService;

public sealed class AuthServiceOptions
{
    public const string SectionName = "AuthService";

    [Required]
    public Uri? BaseAddress { get; set; }

    [Required]
    public string? Key { get; set; }
}

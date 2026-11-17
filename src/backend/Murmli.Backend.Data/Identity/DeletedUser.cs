namespace Murmli.Backend.Data.Identity;

public class DeletedUser
{
    public long Id { get; set; }

    public required string AuthUserId { get; set; }

    public required DateTimeOffset DeletedAt { get; set; }
}

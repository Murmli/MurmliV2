namespace Murmli.Backend.Data.Identity;

public class User
{
    public required Guid Id { get; set; }

    public required string ZitadelUserId { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}

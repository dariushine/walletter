namespace Walletter.Application.Auth;

public class LoginCommand
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool Remember { get; set; }
}

public class CreateTokenCommand
{
    public string Name { get; set; } = string.Empty;
}

using Microsoft.AspNetCore.Identity;
using Territory.Api.Cli;
using Territory.Application.Domain;

namespace Territory.Tests.Api;

public class HashPasswordCommandTests
{
    private const string ValidPassword = "Corretta!Password9";

    [Fact]
    public void Run_ValidMatchingPasswords_PrintsVerifiableHash()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var inputs = new Queue<string?>([ValidPassword, ValidPassword]);

        var exitCode = HashPasswordCommand.Run(output, error, inputs.Dequeue);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        var hash = output.ToString().Trim().Split(Environment.NewLine)[^1];
        var result = new PasswordHasher<AdminUser>().VerifyHashedPassword(null!, hash, ValidPassword);
        Assert.Equal(PasswordVerificationResult.Success, result);
        Assert.DoesNotContain(ValidPassword, output.ToString());
    }

    [Fact]
    public void Run_WeakPassword_FailsWithoutAskingConfirmation()
    {
        var error = new StringWriter();
        var inputs = new Queue<string?>(["weak"]);

        var exitCode = HashPasswordCommand.Run(new StringWriter(), error, inputs.Dequeue);

        Assert.Equal(1, exitCode);
        Assert.Contains("TooShort", error.ToString());
        Assert.Empty(inputs);
    }

    [Fact]
    public void Run_MismatchedConfirmation_Fails()
    {
        var error = new StringWriter();
        var inputs = new Queue<string?>([ValidPassword, ValidPassword + "x"]);

        var exitCode = HashPasswordCommand.Run(new StringWriter(), error, inputs.Dequeue);

        Assert.Equal(1, exitCode);
        Assert.Contains("non coincidono", error.ToString());
    }
}

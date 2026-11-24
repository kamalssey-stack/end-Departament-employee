namespace DepartmentEmployeeManagement.Api.Services;

public interface IAppEmailSender
{
    Task SendEmailConfirmationAsync(string email, string confirmationLink, CancellationToken cancellationToken = default);
    Task SendPasswordResetAsync(string email, string resetLink, CancellationToken cancellationToken = default);
}

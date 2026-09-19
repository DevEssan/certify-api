namespace CertifyApi.Models;

public record Certificate(
    Guid Id,
    string Recipient,
    string Course,
    DateOnly IssuedDate,
    string VerificationUrl
);

public record CreateCertificateRequest(string Recipient, string Course, DateOnly IssuedDate);
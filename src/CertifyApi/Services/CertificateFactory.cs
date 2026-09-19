using CertifyApi.Models;

namespace CertifyApi.Services;

public static class CertificateFactory
{
    public static Certificate Create(Guid id, CreateCertificateRequest request) =>
        new(id, request.Recipient, request.Course, request.IssuedDate, $"/verify/{id}");
}
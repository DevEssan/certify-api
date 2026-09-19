using CertifyApi.Models;
using CertifyApi.Services;
using Xunit;

public class CertificateFactoryTests
{
    [Fact]
    public void Create_ShouldBuildVerificationUrlFromId()
    {
        var id = Guid.NewGuid();
        var request = new CreateCertificateRequest("Anna Andersson", "Molnutveckling", new DateOnly(2026, 9, 18));

        var certificate = CertificateFactory.Create(id, request);

        Assert.Equal(id, certificate.Id);
        Assert.Equal($"/verify/{id}", certificate.VerificationUrl);
        Assert.Equal("Anna Andersson", certificate.Recipient);
        Assert.Equal("Molnutveckling", certificate.Course);
    }
}
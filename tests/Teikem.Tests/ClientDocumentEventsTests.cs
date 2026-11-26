using Teikem.Domain.Constants;
using Teikem.Infrastructure.Notifications;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 — los eventos de los documentos del cliente (ClientDocumentEventProvider): el catálogo se construye con ellos (NotificationEventDefRules.Validate), las claves
/// CONTEXT son válidas, no hay variables bloque, caen en el grupo Ventas sin tocar NotificationEvents.cs y una clave CONTEXT que no declaran no existe.
/// </summary>
public sealed class ClientDocumentEventsTests
{
    private static NotificationEventCatalog Catalog() => new(new INotificationEventProvider[] { new ClientDocumentEventProvider(), new SalesEventProvider() });

    [Fact]
    public void El_catalogo_se_construye_con_los_dos_eventos_y_sus_destinatarios()
    {
        var catalog = Catalog();
        foreach (var code in new[] { ClientDocumentEventCodes.Expiring, ClientDocumentEventCodes.Expired })
        {
            var e = catalog.Find(code)!;
            Assert.NotNull(e);
            Assert.True(code.Length <= 60);
            Assert.Equal((ModuleKeys.Catalog, true, "/clients?client={clientPublicId}&tab=documents"), (e.ModuleKey, e.IsOptional, e.LinkTemplate));
            Assert.Equal(new[] { "client", "clientCode", "documentType", "documentNumber", "validTo", "days", "threshold", "taxNote", "clientPublicId", "documentPublicId", "validToIso" },
                e.Variables.Select(v => v.Name).ToArray());
            Assert.Empty(e.BlockNames);
            Assert.All(e.ContextRecipients!, r => Assert.True(NotificationEventDefRules.IsValidContextKey(r.Key)));
            var sellers = e.ContextRecipient("client_sellers")!;
            Assert.Equal(("CLIENT_SELLERS", "Vendedores del cliente (ruta y preferido)", true, NotificationAudience.Staff), (sellers.Key, sellers.LabelEs, sellers.IsDefault, sellers.Audience));
            var client = e.ContextRecipient(ClientDocumentContextRecipients.Client)!;
            Assert.Equal(("Cliente del documento (correo principal y contactos marcados)", true, NotificationAudience.Customer), (client.LabelEs, client.IsDefault, client.Audience));
            // las variables de los textos por omisión las declara el evento
            var declared = e.Variables.Select(v => v.Name).ToHashSet();
            foreach (var text in new[] { e.DefaultSubjectEs, e.DefaultSubjectEn, e.DefaultBodyEs, e.DefaultBodyEn })
                Assert.All(NotificationRules.VariablesOf(text), name => Assert.Contains(name, declared));
            Assert.Equal(NotificationEventCategories.Sales, NotificationEventCategories.Of(code));
        }
        Assert.Equal(("Documento del cliente por vencer", "Documento del cliente vencido"),
            (catalog.Find(ClientDocumentEventCodes.Expiring)!.NameEs, catalog.Find(ClientDocumentEventCodes.Expired)!.NameEs));
    }

    [Fact]
    public void Una_clave_CONTEXT_que_el_evento_no_declara_no_existe()
    {
        var e = Catalog().Find(ClientDocumentEventCodes.Expiring)!;
        Assert.Null(e.ContextRecipient("FOO"));
        // el mensaje con que NotificationRuleService la rechaza
        Assert.Equal("Este evento no tiene el destinatario 'FOO'.", NotificationRules.UnknownContextRecipientMessage("FOO"));
    }
}

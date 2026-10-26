using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Reiniciar la verificación en dos pasos (MFA) de una persona de portal que perdió su dispositivo: el endpoint solo lo usa quien administra usuarios de portal, con AAL2.
// (Las personas de portal no tienen membresía en la compañía: el reinicio de /api/v1/users/{id}/mfa no llega a ellas.)
public class PortalUserResetMfaTests
{
    private static MethodInfo Action() => typeof(PortalUsersController).GetMethod(nameof(PortalUsersController.ResetMfa))!;

    [Fact]
    public void The_endpoint_is_a_post_under_the_client_with_module_permission_and_aal2()
    {
        var m = Action();
        Assert.Equal("clients/{publicId:guid}/portal-users/{id:int}/reset-mfa", m.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(ModuleKeys.ClientPortal, m.GetCustomAttribute<RequireModuleAttribute>()!.ModuleKey);
        Assert.Equal(Teikem.Domain.Constants.PermissionCatalog.PortalUsersManage, m.GetCustomAttribute<RequirePermissionAttribute>()!.Policy!.Replace(RequirePermissionAttribute.Prefix, string.Empty));
        Assert.NotNull(m.GetCustomAttribute<RequireAal2Attribute>());
    }

    [Fact]
    public void The_conflict_messages_are_exact()
    {
        Assert.Equal("Esta persona todavía no ha entrado al portal: no tiene verificación en dos pasos que reiniciar.", PortalUserService.ResetMfaNotYetMessage);
        Assert.Equal("Esta persona fue dada de baja: no se puede reiniciar su verificación en dos pasos.", PortalUserService.ResetMfaDisabledMessage);
    }
}

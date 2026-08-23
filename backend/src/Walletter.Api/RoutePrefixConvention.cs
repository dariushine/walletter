using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Walletter.Api;

/// <summary>
/// Aplica un prefijo global a todas las rutas (ej. "/api").
/// Equivale a setGlobalPrefix('api') del rework NestJS.
/// Prefija el route de clase si existe; si no (controlador con templates en
/// cada acción, ej. rates), prefija el route de cada acción.
/// </summary>
public static class MvcOptionsExtensions
{
    public static void UseGeneralRoutePrefix(this MvcOptions opts, string prefix)
    {
        opts.Conventions.Add(new RoutePrefixConvention(prefix));
    }

    private sealed class RoutePrefixConvention : IApplicationModelConvention
    {
        private readonly string _prefix;

        public RoutePrefixConvention(string prefix) => _prefix = prefix.TrimEnd('/');

        public void Apply(ApplicationModel application)
        {
            foreach (var controller in application.Controllers)
            {
                var prefixModel = new AttributeRouteModel(new RouteAttribute(_prefix));
                bool hasControllerRoute = controller.Selectors.Any(s => s.AttributeRouteModel != null);

                if (hasControllerRoute)
                {
                    // Prefijo a nivel de controlador: /api + route de clase.
                    foreach (var selector in controller.Selectors)
                    {
                        if (selector.AttributeRouteModel != null)
                        {
                            selector.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(prefixModel, selector.AttributeRouteModel);
                        }
                    }
                }
                else
                {
                    // Sin route de clase: cada acción lleva su template → prefijamos cada acción.
                    foreach (var action in controller.Actions)
                    {
                        foreach (var selector in action.Selectors)
                        {
                            if (selector.AttributeRouteModel != null)
                            {
                                selector.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(prefixModel, selector.AttributeRouteModel);
                            }
                        }
                    }
                }
            }
        }
    }
}

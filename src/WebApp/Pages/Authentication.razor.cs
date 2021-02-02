using Fistix.TaskManager.WebApp.Models.Config;
using Microsoft.AspNetCore.Components;

namespace Fistix.TaskManager.WebApp.Pages
{
    public partial class Authentication : ComponentBase
    {
        [Parameter] public string Action { get; set; }

        [Inject]
        private NavigationManager Navigation { get; set; }
        [Inject]
        private Auth0Config AuthConfig { get; set; }

    }
}

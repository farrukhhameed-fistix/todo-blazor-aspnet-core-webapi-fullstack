using Microsoft.AspNetCore.Components;

namespace Fistix.TaskManager.WebApp.Components
{
    public class RedirectToLogin : ComponentBase
    {
        [Inject]
        protected NavigationManager NavigationManager { get; set; }

        [Parameter] public string LoginUrlString { get; set; }

        protected override void OnInitialized()
        {
            NavigationManager.NavigateTo(LoginUrlString);
        }
    }
}
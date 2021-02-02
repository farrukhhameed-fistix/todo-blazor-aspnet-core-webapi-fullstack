using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApp.Models.Config
{
  public class Auth0Config
  {
    public string Authority { get; set; }
    public string ClientId { get; set; }
    public string Scope { get; set; }
  }
}

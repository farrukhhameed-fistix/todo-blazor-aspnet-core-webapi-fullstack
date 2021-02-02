using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApp.Models
{
  public class ProblemDetails
  {
    
    public string Type { get; set; }    
    public string Title { get; set; }    
    public int? Status { get; set; }    
    public string Detail { get; set; }    
    public string Instance { get; set; }
    public string TraceId { get; set; }
    public Dictionary<string, List<string>> Errors { get; set; }
  }
}

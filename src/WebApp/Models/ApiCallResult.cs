using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApp.Models
{
  public class ApiCallResult<T>
  {
    public string Operation { get; set; }
    public bool IsSucceed { get; set; }
    public string Message { get; set; }
    public T Payload { get; set; }
  }
}

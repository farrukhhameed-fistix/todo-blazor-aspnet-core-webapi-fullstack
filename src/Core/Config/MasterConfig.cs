namespace Fistix.TaskManager.Core.Config
{
  public class MasterConfig
  {
    public Auth0Config Auth0Config { get; set; }
    public AppConfig AppConfig { get; set; }
    public ConnectionStringConfig ConnectionString { get; set; }
    public SwaggerConfig Swagger { get; set; }
    public AzureConfig AzureConfig { get; set; }
  }
}

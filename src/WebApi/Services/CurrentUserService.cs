using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.DataLayer.Repositories;
using Microsoft.AspNetCore.Http;
using System;
using System.Security.Claims;

namespace Fistix.TaskManager.WebApi.Services
{
  public class CurrentUserService : ICurrentUserService
  {
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IRepositoryFactory _repositoryFactory;
    private UserProfile _userProfile = null;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor, IRepositoryFactory repositoryFactory)
    {
      _httpContextAccessor = httpContextAccessor;
      _repositoryFactory = repositoryFactory;
    }

    public string Email => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email);

    public bool HasAdminProfile => UserProfile != null && UserProfile.Id != Guid.Empty && UserProfile.IsAdmin;

    public UserProfile UserProfile
    {
      get
      {
        if (_userProfile == null && string.IsNullOrWhiteSpace(Email) == false)
        {
          _userProfile = _repositoryFactory.UserProfileRepository.GetByEmailAddress(Email).Result;

          if (_userProfile == null)
            _userProfile = new UserProfile();
        }

        return _userProfile;
      }
    }
  }
}

using Core.Models;

namespace Core.Interfaces;

public interface IHelperMethods
{ 
     Task<AppUser> GetLoggedInUserAsync();
     
     Task<AppUser> GetUserByIdAsync(string userId);
}
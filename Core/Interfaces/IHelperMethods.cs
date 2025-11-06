using AmazoniaApi.Core.Models;

namespace AmazoniaApi.Core.Interfaces;

public interface IHelperMethods
{ 
     Task<AppUser> GetLoggedInUserAsync();
     
     Task<AppUser> GetUserByIdAsync(string userId);
}
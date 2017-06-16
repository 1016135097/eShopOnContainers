﻿namespace Microsoft.eShopOnContainers.Services.Catalog.API.Infrastructure
{
    using AspNetCore.Identity;
    using EntityFrameworkCore;
    using Extensions.Logging;
    using global::eShopOnContainers.Identity;
    using global::Identity.API.Data;
    using global::Identity.API.Models;
    using Identity.API.Extensions;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Threading.Tasks;

    public class ApplicationContextSeed
    {
        private readonly IPasswordHasher<ApplicationUser> _passwordHasher;

        public ApplicationContextSeed(IPasswordHasher<ApplicationUser> passwordHasher)
        {
            _passwordHasher = passwordHasher;
        }

        public async Task SeedAsync(IApplicationBuilder applicationBuilder, IHostingEnvironment env, ILoggerFactory loggerFactory, int? retry = 0)
        {
            int retryForAvaiability = retry.Value;
            try
            {
                var log = loggerFactory.CreateLogger("application seed");

                var context = (ApplicationDbContext)applicationBuilder
                    .ApplicationServices.GetService(typeof(ApplicationDbContext));

                context.Database.Migrate();

                var settings = (AppSettings)applicationBuilder
                    .ApplicationServices.GetRequiredService<IOptions<AppSettings>>().Value;

                var useCustomizationData = settings.UseCustomizationData;
                var contentRootPath = env.ContentRootPath;

                if (!context.Users.Any())
                {
                    context.Users.AddRange( useCustomizationData 
                        ? GetUsersFromFile(contentRootPath, log)
                        : GetDefaultUser());

                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                if (retryForAvaiability < 10)
                {
                    retryForAvaiability++;
                    var log = loggerFactory.CreateLogger("catalog seed");
                    log.LogError(ex.Message);
                    await SeedAsync(applicationBuilder, env, loggerFactory, retryForAvaiability);
                }
            }
        }

        private IEnumerable<ApplicationUser> GetUsersFromFile(string contentRootPath, ILogger log)
        {
            string csvFileUsers = Path.Combine(contentRootPath, "Setup", "Users.csv");

            if (!File.Exists(csvFileUsers))
            {
                return GetDefaultUser();
            }

            string[] csvheaders;
            try
            {
                string[] requiredHeaders = {
                    "cardholdername", "cardnumber", "cardtype", "city", "country",
                    "email", "expiration", "lastname", "name", "phonenumber",
                    "username", "zipcode", "state", "street", "securitynumber",
                    "normalizedemail", "normalizedusername", "password"
                };
                csvheaders = GetHeaders(requiredHeaders, csvFileUsers);
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                return GetDefaultUser();
            }

            List<ApplicationUser> users =  File.ReadAllLines(csvFileUsers)
                        .Skip(1) // skip header column
                        .Select(r => r.Split(','))
                        .SelectTry(column => CreateApplicationUser(column, csvheaders))
                        .OnCaughtException(ex => { log.LogError(ex.Message); return null; })
                        .Where(x => x != null)
                        .ToList();

            return users;
        }

        private ApplicationUser CreateApplicationUser(string[] column, string[] headers)
        {
            if (column.Count() != headers.Count())
            {
                throw new Exception($"column count '{column.Count()}' not the same as headers count'{headers.Count()}'");
            }

            string cardtypeString = column[Array.IndexOf(headers, "cardtype")].Trim();
            if (!int.TryParse(cardtypeString, out int cardtype))
            {
                throw new Exception($"cardtype='{cardtypeString}' is not a number");
            }

            var user =  new ApplicationUser
            {
                CardHolderName = column[Array.IndexOf(headers, "cardholdername")].Trim(),
                CardNumber = column[Array.IndexOf(headers, "cardnumber")].Trim(),
                CardType = cardtype,
                City = column[Array.IndexOf(headers, "city")].Trim(),
                Country = column[Array.IndexOf(headers, "country")].Trim(),
                Email = column[Array.IndexOf(headers, "email")].Trim(),
                Expiration = column[Array.IndexOf(headers, "expiration")].Trim(),
                Id = Guid.NewGuid().ToString(),
                LastName = column[Array.IndexOf(headers, "lastname")].Trim(),
                Name = column[Array.IndexOf(headers, "name")].Trim(),
                PhoneNumber = column[Array.IndexOf(headers, "phonenumber")].Trim(),
                UserName = column[Array.IndexOf(headers, "username")].Trim(),
                ZipCode = column[Array.IndexOf(headers, "zipcode")].Trim(),
                State = column[Array.IndexOf(headers, "state")].Trim(),
                Street = column[Array.IndexOf(headers, "street")].Trim(),
                SecurityNumber = column[Array.IndexOf(headers, "securitynumber")].Trim(),
                NormalizedEmail = column[Array.IndexOf(headers, "normalizedemail")].Trim(),
                NormalizedUserName = column[Array.IndexOf(headers, "normalizedusername")].Trim(),
                SecurityStamp = Guid.NewGuid().ToString("D"),
                PasswordHash = column[Array.IndexOf(headers, "password")].Trim(), // Note: This is the password
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, user.PasswordHash);

            return user;
        }

        private IEnumerable<ApplicationUser> GetDefaultUser()
        {
            var user = 
            new ApplicationUser()
            {
                CardHolderName = "DemoUser",
                CardNumber = "4012888888881881",
                CardType = 1,
                City = "Redmond",
                Country = "U.S.",
                Email = "demouser@microsoft.com",
                Expiration = "12/20",
                Id = Guid.NewGuid().ToString(), 
                LastName = "DemoLastName", 
                Name = "DemoUser", 
                PhoneNumber = "1234567890", 
                UserName = "demouser@microsoft.com", 
                ZipCode = "98052", 
                State = "WA", 
                Street = "15703 NE 61st Ct", 
                SecurityNumber = "535", 
                NormalizedEmail = "DEMOUSER@MICROSOFT.COM", 
                NormalizedUserName = "DEMOUSER@MICROSOFT.COM", 
                SecurityStamp = Guid.NewGuid().ToString("D"),
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, "Pass@word1");

            return new List<ApplicationUser>()
            {
                user
            };
        }

        static string[] GetHeaders(string[] requiredHeaders, string csvfile)
        {
            string[] csvheaders = File.ReadLines(csvfile).First().ToLowerInvariant().Split(',');

            if (csvheaders.Count() != requiredHeaders.Count())
            {
                throw new Exception($"requiredHeader count '{ requiredHeaders.Count()}' is different then read header '{csvheaders.Count()}'");
            }

            foreach (var requiredHeader in requiredHeaders)
            {
                if (!csvheaders.Contains(requiredHeader))
                {
                    throw new Exception($"does not contain required header '{requiredHeader}'");
                }
            }

            return csvheaders;
        }
    }
}

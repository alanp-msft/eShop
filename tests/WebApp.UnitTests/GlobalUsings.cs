global using System;
global using System.Linq;
global using System.Net;
global using System.Net.Http;
global using System.Net.Http.Json;
global using System.Threading;
global using System.Threading.Tasks;
global using Bunit;
global using eShop.WebApp.Components.Pages.User;
global using eShop.WebApp.Services;
global using Microsoft.AspNetCore.Components;
global using Microsoft.AspNetCore.Components.Forms;
global using Microsoft.AspNetCore.Components.Rendering;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]

global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.Logging;
global using NSubstitute;
global using eShop.Catalog.API.Infrastructure;
global using eShop.Catalog.API.IntegrationEvents.EventHandling;
global using eShop.Catalog.API.IntegrationEvents.Events;
global using eShop.Catalog.API.Model;
global using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]

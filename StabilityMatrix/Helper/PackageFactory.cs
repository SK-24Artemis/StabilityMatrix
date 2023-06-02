﻿using System.Collections.Generic;
using System.Linq;
using StabilityMatrix.Models;
using StabilityMatrix.Models.Packages;

namespace StabilityMatrix.Helper;

public class PackageFactory : IPackageFactory
{
    /// <summary>
    /// Mapping of package.Name to package
    /// </summary>
    private readonly Dictionary<string, BasePackage> basePackages;

    public PackageFactory(IEnumerable<BasePackage> basePackages)
    {
        this.basePackages = basePackages.ToDictionary(x => x.Name);
    }
    
    public IEnumerable<BasePackage> GetAllAvailablePackages()
    {
        return basePackages.Values;
    }

    public BasePackage? FindPackageByName(string packageName)
    {
        return basePackages.GetValueOrDefault(packageName);
    }
}
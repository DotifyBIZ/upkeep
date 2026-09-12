using Upkeep.App.Core.Services;

namespace Upkeep.App.Core.Tests.Services;

public class ServiceClassifierTests
{
    private static ServiceInfo Service(
        string name,
        bool isMicrosoft = true,
        bool hasRunningDependents = false,
        bool isRunning = false) =>
        new(name, $"{name} display name", "Manual", isRunning, isMicrosoft, hasRunningDependents);

    [Theory]
    [InlineData("DiagTrack")]
    [InlineData("MapsBroker")]
    [InlineData("XblAuthManager")]
    [InlineData("Fax")]
    [InlineData("RemoteRegistry")]
    public void Classify_CuratedService_IsCommonlySafe(string name) =>
        Assert.Equal(ServiceTier.CommonlySafe, ServiceClassifier.Classify(Service(name)));

    [Theory]
    [InlineData("RpcSs")]
    [InlineData("DcomLaunch")]
    [InlineData("ProfSvc")]
    [InlineData("WinDefend")]
    [InlineData("wuauserv")]
    [InlineData("Dnscache")]
    public void Classify_ServiceWindowsNeeds_IsLocked(string name) =>
        Assert.Equal(ServiceTier.Locked, ServiceClassifier.Classify(Service(name)));

    [Fact]
    public void Classify_MicrosoftServiceNotOnAnyList_IsLeaveAlone() =>
        Assert.Equal(ServiceTier.LeaveAlone, ServiceClassifier.Classify(Service("SomeWindowsService")));

    [Fact]
    public void Classify_NonMicrosoftService_IsThirdParty() =>
        Assert.Equal(ServiceTier.ThirdParty, ServiceClassifier.Classify(Service("SomeVendorUpdater", isMicrosoft: false)));

    [Fact]
    public void Classify_ServiceSomethingRunningDependsOn_IsLocked()
    {
        // Turning it off stops whatever is running on top of it, whatever list it is on.
        var service = Service("SomeVendorHelper", isMicrosoft: false, hasRunningDependents: true);

        Assert.Equal(ServiceTier.Locked, ServiceClassifier.Classify(service));
    }

    [Fact]
    public void Classify_CuratedServiceWithRunningDependents_IsStillLocked()
    {
        // The curated list says "safe in the usual case"; a live dependency says this isn't it.
        var service = Service("DiagTrack", hasRunningDependents: true);

        Assert.Equal(ServiceTier.Locked, ServiceClassifier.Classify(service));
    }

    [Fact]
    public void CanChange_LockedService_IsRefused()
    {
        Assert.False(ServiceClassifier.CanChange(Service("RpcSs")));
        Assert.True(ServiceClassifier.CanChange(Service("Fax")));
        Assert.True(ServiceClassifier.CanChange(Service("SomeVendorUpdater", isMicrosoft: false)));
    }

    [Fact]
    public void WhatBreaksKey_CuratedService_HasItsOwnLine()
    {
        // Every curated entry states what stops working; that is what earns it the tier.
        Assert.Equal("ServiceBreaksFax", ServiceClassifier.WhatBreaksKey(Service("Fax")));
        Assert.Equal("ServiceBreaksDiagTrack", ServiceClassifier.WhatBreaksKey(Service("DiagTrack")));
    }

    [Fact]
    public void WhatBreaksKey_AnythingElse_HasNothingHonestToSay() =>
        Assert.Null(ServiceClassifier.WhatBreaksKey(Service("SomeWindowsService")));

    [Fact]
    public void CommonlySafe_IsTheShortCuratedListItWasApprovedAs()
    {
        // The tier exists because each entry was reviewed by name. A list that grows by inference
        // would be a different, less defensible product.
        Assert.Equal(12, ServiceCatalog.CommonlySafe.Count);
        Assert.All(ServiceCatalog.CommonlySafe, service => Assert.False(string.IsNullOrWhiteSpace(service.WhatBreaksKey)));
    }

    [Fact]
    public void CommonlySafe_AndLocked_NeverOverlap()
    {
        foreach (var curated in ServiceCatalog.CommonlySafe)
        {
            Assert.DoesNotContain(curated.ServiceName, ServiceCatalog.Locked);
        }
    }

    [Fact]
    public void Classify_IsCaseInsensitive_BecauseWindowsIs()
    {
        Assert.Equal(ServiceTier.CommonlySafe, ServiceClassifier.Classify(Service("diagtrack")));
        Assert.Equal(ServiceTier.Locked, ServiceClassifier.Classify(Service("RPCSS")));
    }
}

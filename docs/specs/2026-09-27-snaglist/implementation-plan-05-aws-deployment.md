# SnagList AWS Deployment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deploy `api`/`mcp` as Lambda functions behind API Gateway and `web` as a CloudFront-fronted
S3 static site, authenticated against Entra ID, backed by RDS PostgreSQL and S3, per
07-deployment-aws.md — provisioned by Terraform.

**Architecture:** The same container images every other deployment uses (Plan 1's Global
Constraints: "one Dockerfile per service, everywhere"), packaged for Lambda via the container-image
runtime. Two things this plan discovers were never actually finished in earlier plans, despite
being *described* in 02-solution-architecture.md: the Lambda hosting shim (Program.cs always just
called `app.Run()` — Kestrel, never the Lambda handler) and `IBlobStorage`/`IEmailSender` being
hardcoded to their local/home-lab implementations inside `AddSnagListInfrastructure` (MinIO
credentials, SMTP) with no way to select S3/SES instead. Both get fixed here, config-driven, so the
*same* `AddSnagListInfrastructure` call and the *same* `Program.cs` serve all three deployments —
only environment variables (set by Terraform, Task 8) differ.

**Tech Stack:** `Amazon.Lambda.AspNetCoreServer.Hosting`, `AWSSDK.SimpleEmailV2`, Terraform (AWS
provider), `NSubstitute` (justified narrowly in Task 1 — see its own note).

**Spec:** [docs/specs/2026-09-27-snaglist/](README.md), primarily 07-deployment-aws.md. Builds on
[implementation-plan-01-core-domain-api.md](implementation-plan-01-core-domain-api.md) (`api`,
`IBlobStorage`/`IEmailSender` ports) and [implementation-plan-02-mcp-server.md](implementation-plan-02-mcp-server.md)
(`mcp`, `AddSnagListInfrastructure`). Plan 3's `web` is deployed here as a static site with no
code changes of its own — Plan 4 (home-lab) is independent of this one.

## Global Constraints

- No separate build artifact for AWS — `api`/`mcp` ship the identical container image used by local
  docker and home-lab; only the Lambda Runtime Interface Client wrapper and environment variables
  differ (Plan 1, Task 2's original design intent, finished in Task 2 below).
- Entra ID integration is bearer-JWT validation against Entra's JWKS directly — never OIDC
  federation into AWS IAM (04-security-and-authentication.md, 07-deployment-aws.md).
- No secret (DB credentials, token-signing material) ever appears in a Lambda environment variable
  in plaintext — Secrets Manager references only (07-deployment-aws.md).
- Every Terraform resource that can be encrypted, is (RDS storage, S3 buckets, Secrets Manager
  entries) — matching 07-deployment-aws.md's explicit call-outs.
- This plan does not run `terraform apply` against a real AWS account for you — every verification
  step through Task 8 is `terraform validate`/`plan`, which cost nothing and touch nothing. Task 9's
  manual step is the one that costs real money and needs real AWS credentials; it says so.
- No placeholder code, no `TODO`s left in committed code or Terraform — every task ships working,
  verified code and configuration.

---

## Task 1: Provider-selectable blob storage and email — `SesEmailSender`, config-driven wiring

**On the test approach for `SesEmailSender`:** every other port implementation in this project uses
a hand-written fake or a real instance via Testcontainers (Plans 1–4) — never a mocking library.
`IAmazonSimpleEmailServiceV2` breaks that pattern's assumption: it's a third-party SDK interface
with dozens of members this project doesn't own and has no reason to fully implement by hand (a
hand-written fake would be dozens of `NotImplementedException` stubs around the one method that
matters). That's the specific, narrow case a mocking library exists for. `NSubstitute` is used here
and nowhere else in this codebase for that reason — this is not a change of testing philosophy.

**Files:**
- Create: `src/SnagList.Infrastructure/Email/SesEmailSenderOptions.cs`
- Create: `src/SnagList.Infrastructure/Email/SesEmailSender.cs`
- Test: `tests/SnagList.Infrastructure.Tests/Email/SesEmailSenderTests.cs`
- Modify: `src/SnagList.Infrastructure/ServiceCollectionExtensions.cs` (Plan 2, Task 1) — branch
  blob storage and email registration on `Storage:Provider`/`Email:Provider` config
- Test: `tests/SnagList.Infrastructure.Tests/ServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: `IEmailSender` (Plan 1, Task 5).
- Produces: `SesEmailSender` — selected by `Email:Provider=Ses`, wired into `Program.cs` (already
  calling `AddSnagListInfrastructure`, Plan 2, Task 3 — no `Program.cs` change needed for this task,
  only for Task 2's Lambda shim) via Task 8's Terraform-set environment variables.

- [ ] **Step 1: Add packages**

```bash
dotnet add src/SnagList.Infrastructure package AWSSDK.SimpleEmailV2
dotnet add tests/SnagList.Infrastructure.Tests package NSubstitute
```

- [ ] **Step 2: Write the failing `SesEmailSender` test**

```csharp
namespace SnagList.Infrastructure.Tests.Email;

using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using NSubstitute;
using SnagList.Infrastructure.Email;
using Xunit;

public class SesEmailSenderTests
{
    [Fact]
    public async Task SendAsync_calls_SES_with_the_expected_fields()
    {
        var sesClient = Substitute.For<IAmazonSimpleEmailServiceV2>();
        var sender = new SesEmailSender(sesClient, new SesEmailSenderOptions { FromAddress = "snaglist@example.com" });

        await sender.SendAsync("maintenance@example.com", "New Snag reported", "A Snag was reported.", default);

        await sesClient.Received(1).SendEmailAsync(
            Arg.Is<SendEmailRequest>(r =>
                r.FromEmailAddress == "snaglist@example.com" &&
                r.Destination.ToAddresses.Contains("maintenance@example.com") &&
                r.Content.Simple.Subject.Data == "New Snag reported" &&
                r.Content.Simple.Body.Text.Data == "A Snag was reported."),
            Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter SesEmailSenderTests`
Expected: FAIL — `SesEmailSender`/`SesEmailSenderOptions` don't exist.

- [ ] **Step 4: Implement**

```csharp
namespace SnagList.Infrastructure.Email;

public sealed class SesEmailSenderOptions
{
    public string FromAddress { get; set; } = "";
}
```

```csharp
namespace SnagList.Infrastructure.Email;

using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using SnagList.Application.Abstractions;

public sealed class SesEmailSender(IAmazonSimpleEmailServiceV2 sesClient, SesEmailSenderOptions options) : IEmailSender
{
    public Task SendAsync(string toAddress, string subject, string body, CancellationToken ct) =>
        sesClient.SendEmailAsync(new SendEmailRequest
        {
            FromEmailAddress = options.FromAddress,
            Destination = new Destination { ToAddresses = [toAddress] },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = subject },
                    Body = new Body { Text = new Content { Data = body } },
                },
            },
        }, ct);
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter SesEmailSenderTests`
Expected: PASS.

- [ ] **Step 6: Write the failing provider-selection test**

```csharp
namespace SnagList.Infrastructure.Tests;

using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SnagList.Application.Abstractions;
using SnagList.Infrastructure;
using SnagList.Infrastructure.Email;
using SnagList.Infrastructure.Storage;
using Xunit;

public class ServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfig(IDictionary<string, string?> overrides)
    {
        var baseValues = new Dictionary<string, string?>
        {
            ["ConnectionStrings:SnagList"] = "Host=localhost;Database=x;Username=x;Password=x",
            ["Storage:BucketName"] = "test-bucket",
        };
        foreach (var (key, value) in overrides) baseValues[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(baseValues).Build();
    }

    [Fact]
    public void Defaults_to_S3Compatible_storage_and_Smtp_email_when_no_provider_is_configured()
    {
        var services = new ServiceCollection();
        services.AddSnagListInfrastructure(BuildConfig(new Dictionary<string, string?>
        {
            ["Storage:AccessKey"] = "x", ["Storage:SecretKey"] = "x", ["Storage:ServiceUrl"] = "http://localhost:9000",
            ["Email:Host"] = "localhost", ["Email:FromAddress"] = "x@example.com",
        }));
        using var provider = services.BuildServiceProvider();

        Assert.IsType<S3CompatibleBlobStorage>(provider.GetRequiredService<IBlobStorage>());
        Assert.IsType<SmtpEmailSender>(provider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void Selects_real_S3_credentials_and_SES_when_configured_for_AWS()
    {
        var services = new ServiceCollection();
        services.AddSnagListInfrastructure(BuildConfig(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "S3",
            ["Email:Provider"] = "Ses",
            ["Email:FromAddress"] = "snaglist@example.com",
        }));
        using var provider = services.BuildServiceProvider();

        Assert.IsType<S3CompatibleBlobStorage>(provider.GetRequiredService<IBlobStorage>());
        Assert.IsType<SesEmailSender>(provider.GetRequiredService<IEmailSender>());
    }
}
```

- [ ] **Step 7: Run test to verify it fails**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter ServiceCollectionExtensionsTests`
Expected: FAIL — `Storage:Provider`/`Email:Provider` aren't read yet; the `"S3"`/`"Ses"` case throws
on the still-required MinIO/SMTP config keys this test deliberately omits.

- [ ] **Step 8: Rewrite `AddSnagListInfrastructure`'s storage and email sections**

Replace the `Storage`/`IAmazonS3`/`IBlobStorage` block and the `SmtpEmailSenderOptions`/
`IEmailSender` block (Plan 2, Task 1) with:

```csharp
var storage = configuration.GetSection("Storage");
var storageBucket = storage["BucketName"] ?? throw new InvalidOperationException("Storage:BucketName is required.");
switch (storage["Provider"] ?? "S3Compatible")
{
    case "S3Compatible":
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            new BasicAWSCredentials(
                storage["AccessKey"] ?? throw new InvalidOperationException("Storage:AccessKey is required."),
                storage["SecretKey"] ?? throw new InvalidOperationException("Storage:SecretKey is required.")),
            new AmazonS3Config
            {
                ServiceURL = storage["ServiceUrl"] ?? throw new InvalidOperationException("Storage:ServiceUrl is required."),
                ForcePathStyle = true,
            }));
        break;
    case "S3":
        // Real S3: credentials come from the Lambda execution role via the default AWS credential
        // chain, never from configuration — there is nothing else to set here.
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
        break;
    default:
        throw new InvalidOperationException($"Unknown Storage:Provider '{storage["Provider"]}'.");
}
services.AddSingleton<IBlobStorage>(sp => new S3CompatibleBlobStorage(sp.GetRequiredService<IAmazonS3>(), storageBucket));

switch (configuration["Email:Provider"] ?? "Smtp")
{
    case "Smtp":
        services.Configure<SmtpEmailSenderOptions>(configuration.GetSection("Email"));
        services.AddSingleton<IEmailSender>(sp => new SmtpEmailSender(sp.GetRequiredService<IOptions<SmtpEmailSenderOptions>>().Value));
        break;
    case "Ses":
        services.AddSingleton<IAmazonSimpleEmailServiceV2>(_ => new AmazonSimpleEmailServiceV2Client());
        services.Configure<SesEmailSenderOptions>(configuration.GetSection("Email"));
        services.AddSingleton<IEmailSender>(sp => new SesEmailSender(
            sp.GetRequiredService<IAmazonSimpleEmailServiceV2>(), sp.GetRequiredService<IOptions<SesEmailSenderOptions>>().Value));
        break;
    default:
        throw new InvalidOperationException($"Unknown Email:Provider '{configuration["Email:Provider"]}'.");
}
```

Add `using Amazon.SimpleEmailV2;` to the file's `using` block. Every existing call site
(`SnagList.Api`, `SnagList.Mcp`, every test factory across Plans 1–4) omits `Storage:Provider`/
`Email:Provider` entirely, so it defaults to `"S3Compatible"`/`"Smtp"` — identical behavior to
before this task, unchanged.

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter ServiceCollectionExtensionsTests`
Expected: PASS (2 tests).

- [ ] **Step 10: Run the full solution test suite to confirm no regressions**

Run: `dotnet test`
Expected: PASS — every project, Plans 1–4 unaffected (all their config omits the new keys, so the
default branch is what they've always exercised).

- [ ] **Step 11: Commit**

```bash
git add src/SnagList.Infrastructure tests/SnagList.Infrastructure.Tests
git commit -m "feat(infrastructure): add SesEmailSender and provider-selectable storage/email wiring"
```

---

## Task 2: Lambda container-image hosting for `api` and `mcp`

Smaller than 02-solution-architecture.md's prose implied: `Amazon.Lambda.AspNetCoreServer.Hosting`
does its own `AWS_LAMBDA_FUNCTION_NAME` detection internally — `AddAWSLambdaHosting(...)` is a
single line next to the other `builder.Services.Add...` calls, and `app.Run()` doesn't change at
all. Outside Lambda (local docker, home-lab) it's inert; inside Lambda, it transparently replaces
the Kestrel server loop with the Lambda runtime client loop. There is no manual environment-variable
branch to write.

**Files:**
- Modify: `src/SnagList.Api/Program.cs` — add the hosting call
- Modify: `src/SnagList.Api/SnagList.Api.csproj` — add the package
- Modify: `src/SnagList.Mcp/Program.cs` — add the hosting call
- Modify: `src/SnagList.Mcp/SnagList.Mcp.csproj` — add the package

**Interfaces:** none — this changes hosting behavior only under a real Lambda runtime, which no
automated test in this repo can invoke; Task 9's manual verification is where it's actually proven.

- [ ] **Step 1: Add the package to both projects**

```bash
dotnet add src/SnagList.Api package Amazon.Lambda.AspNetCoreServer.Hosting
dotnet add src/SnagList.Mcp package Amazon.Lambda.AspNetCoreServer.Hosting
```

- [ ] **Step 2: Add one line to each `Program.cs`**

In `src/SnagList.Api/Program.cs`, alongside the other `builder.Services.Add...` calls (anywhere
before `builder.Build()`):

```csharp
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
```

Add `using Amazon.Lambda.AspNetCoreServer.Hosting;` to the top of the file. Do the identical
addition to `src/SnagList.Mcp/Program.cs`.

- [ ] **Step 3: Run the existing host smoke tests to confirm the non-Lambda path is unaffected**

Run: `dotnet test tests/SnagList.Api.Tests --filter HealthEndpointTests`
Run: `dotnet test tests/SnagList.Mcp.Tests --filter HealthEndpointTests`
Expected: PASS — identical to before this task. Outside a Lambda runtime, `AddAWSLambdaHosting`
changes nothing observable; this is the one thing about it this repo can verify without deploying.

- [ ] **Step 4: Commit**

```bash
git add src/SnagList.Api src/SnagList.Mcp
git commit -m "feat(deploy): add Lambda container-image hosting to api and mcp"
```

---

## Task 3: `SnagList.Api.Auth.EntraId` and the `Auth:Provider` switch

Mirrors `SnagList.Api.Auth.Local` (Plan 1, Task 15) exactly in shape — a bearer-JWT scheme
validated against a tenant-specific JWKS endpoint — but there is no lightweight, official Entra ID
container the way Keycloak has one, so unlike Task 1 in Plan 1 this can't be integration-tested
against a real instance here. What *is* testable without a real Azure tenant: the fail-fast config
validation, and that the DI graph for `Auth:Provider=EntraId` constructs without error (the same
boot-smoke-test style as `HealthEndpointTests`, Plan 1 Task 16). The actual token-validation path
against a real Entra tenant is Task 9's manual verification, because nothing else can stand in for
one.

**Files:**
- Create: `src/SnagList.Api.Auth.EntraId/SnagList.Api.Auth.EntraId.csproj`
- Create: `src/SnagList.Api.Auth.EntraId/EntraIdAuthenticationExtensions.cs`
- Test: `tests/SnagList.Api.Auth.EntraId.Tests/SnagList.Api.Auth.EntraId.Tests.csproj`
- Test: `tests/SnagList.Api.Auth.EntraId.Tests/EntraIdAuthenticationExtensionsTests.cs`
- Modify: `src/SnagList.Api/Program.cs` — replace the unconditional `AddKeycloakAuthentication` call
  with an `Auth:Provider` switch
- Modify: `src/SnagList.Mcp/Program.cs` — same switch
- Test: `tests/SnagList.Api.Tests/EntraIdHostTests.cs`

**Interfaces:**
- Produces: `EntraIdAuthenticationExtensions.AddEntraIdAuthentication(configuration)` — selected by
  `Auth:Provider=EntraId`, wired via Task 8's Terraform-set Lambda environment variables.

- [ ] **Step 1: Scaffold the project**

```bash
dotnet new classlib -o src/SnagList.Api.Auth.EntraId -n SnagList.Api.Auth.EntraId
rm src/SnagList.Api.Auth.EntraId/Class1.cs
dotnet add src/SnagList.Api.Auth.EntraId package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/SnagList.Api reference src/SnagList.Api.Auth.EntraId
dotnet add src/SnagList.Mcp reference src/SnagList.Api.Auth.EntraId

dotnet new xunit -o tests/SnagList.Api.Auth.EntraId.Tests -n SnagList.Api.Auth.EntraId.Tests
rm tests/SnagList.Api.Auth.EntraId.Tests/UnitTest1.cs
dotnet add tests/SnagList.Api.Auth.EntraId.Tests reference src/SnagList.Api.Auth.EntraId

dotnet sln add src/SnagList.Api.Auth.EntraId tests/SnagList.Api.Auth.EntraId.Tests
```

- [ ] **Step 2: Write the failing config-validation tests**

```csharp
namespace SnagList.Api.Auth.EntraId.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SnagList.Api.Auth.EntraId;
using Xunit;

public class EntraIdAuthenticationExtensionsTests
{
    private static IConfiguration Config(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Throws_when_TenantId_is_missing()
    {
        var services = new ServiceCollection();
        var config = Config(new Dictionary<string, string?> { ["Auth:EntraId:Audience"] = "snaglist-api" });

        Assert.Throws<InvalidOperationException>(() => services.AddEntraIdAuthentication(config));
    }

    [Fact]
    public void Throws_when_Audience_is_missing()
    {
        var services = new ServiceCollection();
        var config = Config(new Dictionary<string, string?> { ["Auth:EntraId:TenantId"] = "11111111-1111-1111-1111-111111111111" });

        Assert.Throws<InvalidOperationException>(() => services.AddEntraIdAuthentication(config));
    }

    [Fact]
    public void Succeeds_and_registers_JwtBearer_when_both_are_present()
    {
        var services = new ServiceCollection();
        var config = Config(new Dictionary<string, string?>
        {
            ["Auth:EntraId:TenantId"] = "11111111-1111-1111-1111-111111111111",
            ["Auth:EntraId:Audience"] = "snaglist-api",
        });

        services.AddEntraIdAuthentication(config);

        Assert.Contains(services, d => d.ServiceType == typeof(Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Api.Auth.EntraId.Tests`
Expected: FAIL — `AddEntraIdAuthentication` does not exist.

- [ ] **Step 4: Implement**

```csharp
namespace SnagList.Api.Auth.EntraId;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class EntraIdAuthenticationExtensions
{
    public static IServiceCollection AddEntraIdAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var tenantId = configuration["Auth:EntraId:TenantId"]
            ?? throw new InvalidOperationException("Auth:EntraId:TenantId is required.");
        var audience = configuration["Auth:EntraId:Audience"]
            ?? throw new InvalidOperationException("Auth:EntraId:Audience is required.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
                options.Audience = audience;
                options.RequireHttpsMetadata = true;
            });

        return services;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Api.Auth.EntraId.Tests`
Expected: PASS (3 tests).

- [ ] **Step 6: Add the `Auth:Provider` switch to `Program.cs` (both `Api` and `Mcp`)**

Replace `builder.Services.AddKeycloakAuthentication(builder.Configuration);` in both `Program.cs`
files with:

```csharp
switch (builder.Configuration["Auth:Provider"] ?? "Local")
{
    case "Local":
        builder.Services.AddKeycloakAuthentication(builder.Configuration);
        break;
    case "EntraId":
        builder.Services.AddEntraIdAuthentication(builder.Configuration);
        break;
    default:
        throw new InvalidOperationException($"Unknown Auth:Provider '{builder.Configuration["Auth:Provider"]}'.");
}
```

Add `using SnagList.Api.Auth.EntraId;` to both files. Every existing deployment (local docker,
home-lab) omits `Auth:Provider` entirely, defaulting to `"Local"` — unchanged behavior.

- [ ] **Step 7: Write the failing `EntraIdHostTests` boot-smoke test**

```csharp
namespace SnagList.Api.Tests;

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class EntraIdHostTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Host_boots_with_Auth_Provider_set_to_EntraId()
    {
        var client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:SnagList", "Host=localhost;Database=x;Username=x;Password=x");
            builder.UseSetting("Storage:BucketName", "test-bucket");
            builder.UseSetting("Storage:Provider", "S3");
            builder.UseSetting("Email:Provider", "Ses");
            builder.UseSetting("Email:FromAddress", "snaglist@example.com");
            builder.UseSetting("Notifications:MaintenanceTeamEmail", "maintenance@example.com");
            builder.UseSetting("Auth:Provider", "EntraId");
            builder.UseSetting("Auth:EntraId:TenantId", "11111111-1111-1111-1111-111111111111");
            builder.UseSetting("Auth:EntraId:Audience", "snaglist-api");
        }).CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 8: Run test to verify it fails, then passes**

Run: `dotnet test tests/SnagList.Api.Tests --filter EntraIdHostTests`
Expected: FAIL (missing type/using) until Step 6 lands, then PASS.

Run: `dotnet test tests/SnagList.Api.Tests --filter HealthEndpointTests`
Expected: still PASS — the default `Auth:Provider=Local` path (that test's own config) is
unaffected by this task.

- [ ] **Step 9: Commit**

```bash
git add src/SnagList.Api.Auth.EntraId src/SnagList.Api/Program.cs src/SnagList.Mcp/Program.cs \
  tests/SnagList.Api.Auth.EntraId.Tests tests/SnagList.Api.Tests/EntraIdHostTests.cs SnagList.sln
git commit -m "feat(auth): add SnagList.Api.Auth.EntraId and the Auth:Provider switch"
```

---

## Task 4: Terraform state backend and networking

The state backend is a chicken-and-egg case — a remote backend can't provision the bucket it will
then store its own state in — so it's a separate root module, applied once, with local state, before
the main configuration (Task 8) ever runs.

**Files:**
- Create: `deploy/aws/bootstrap/main.tf`
- Create: `deploy/aws/bootstrap/variables.tf`
- Create: `deploy/aws/bootstrap/outputs.tf`
- Create: `deploy/aws/modules/networking/main.tf`
- Create: `deploy/aws/modules/networking/variables.tf`
- Create: `deploy/aws/modules/networking/outputs.tf`

**Interfaces:**
- Produces: the `state_bucket_name`/`lock_table_name` outputs Task 8's root `backend "s3"` block
  references; the networking module's `vpc_id`/`private_subnet_ids`/`lambda_security_group_id`/
  `database_security_group_id` outputs, consumed by Tasks 5 and 7.

- [ ] **Step 1: Write the bootstrap module**

```hcl
# deploy/aws/bootstrap/main.tf
terraform {
  required_version = ">= 1.9"
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 5.0" }
  }
}

provider "aws" {
  region = var.aws_region
}

resource "aws_s3_bucket" "terraform_state" {
  bucket = var.state_bucket_name

  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_s3_bucket_versioning" "terraform_state" {
  bucket = aws_s3_bucket.terraform_state.id
  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "terraform_state" {
  bucket = aws_s3_bucket.terraform_state.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_public_access_block" "terraform_state" {
  bucket                  = aws_s3_bucket.terraform_state.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_dynamodb_table" "terraform_locks" {
  name         = var.lock_table_name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "LockID"

  attribute {
    name = "LockID"
    type = "S"
  }
}
```

```hcl
# deploy/aws/bootstrap/variables.tf
variable "aws_region" {
  type = string
}

variable "state_bucket_name" {
  type = string
}

variable "lock_table_name" {
  type    = string
  default = "snaglist-terraform-locks"
}
```

```hcl
# deploy/aws/bootstrap/outputs.tf
output "state_bucket_name" {
  value = aws_s3_bucket.terraform_state.bucket
}

output "lock_table_name" {
  value = aws_dynamodb_table.terraform_locks.name
}
```

- [ ] **Step 2: Write the networking module**

```hcl
# deploy/aws/modules/networking/main.tf
resource "aws_vpc" "main" {
  cidr_block           = var.vpc_cidr
  enable_dns_support   = true
  enable_dns_hostnames = true
  tags                 = { Name = "${var.name_prefix}-vpc" }
}

resource "aws_subnet" "public" {
  count                   = length(var.availability_zones)
  vpc_id                  = aws_vpc.main.id
  cidr_block              = cidrsubnet(var.vpc_cidr, 4, count.index)
  availability_zone       = var.availability_zones[count.index]
  map_public_ip_on_launch = true
  tags                    = { Name = "${var.name_prefix}-public-${count.index}" }
}

resource "aws_subnet" "private" {
  count             = length(var.availability_zones)
  vpc_id            = aws_vpc.main.id
  cidr_block        = cidrsubnet(var.vpc_cidr, 4, count.index + length(var.availability_zones))
  availability_zone = var.availability_zones[count.index]
  tags              = { Name = "${var.name_prefix}-private-${count.index}" }
}

resource "aws_internet_gateway" "main" {
  vpc_id = aws_vpc.main.id
  tags   = { Name = "${var.name_prefix}-igw" }
}

# Required for Entra ID JWKS egress (07-deployment-aws.md) — Entra's OIDC endpoint has no VPC
# interface endpoint, so private-subnet Lambdas need a NAT gateway to reach it over the public
# internet. This is also the dominant cost driver noted in that same spec section.
resource "aws_eip" "nat" {
  domain = "vpc"
}

resource "aws_nat_gateway" "main" {
  allocation_id = aws_eip.nat.id
  subnet_id     = aws_subnet.public[0].id
  tags          = { Name = "${var.name_prefix}-nat" }
  depends_on    = [aws_internet_gateway.main]
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.main.id
  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.main.id
  }
  tags = { Name = "${var.name_prefix}-public-rt" }
}

resource "aws_route_table_association" "public" {
  count          = length(aws_subnet.public)
  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}

resource "aws_route_table" "private" {
  vpc_id = aws_vpc.main.id
  route {
    cidr_block     = "0.0.0.0/0"
    nat_gateway_id = aws_nat_gateway.main.id
  }
  tags = { Name = "${var.name_prefix}-private-rt" }
}

resource "aws_route_table_association" "private" {
  count          = length(aws_subnet.private)
  subnet_id      = aws_subnet.private[count.index].id
  route_table_id = aws_route_table.private.id
}

resource "aws_security_group" "lambda" {
  name_prefix = "${var.name_prefix}-lambda-"
  vpc_id      = aws_vpc.main.id

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_security_group" "database" {
  name_prefix = "${var.name_prefix}-db-"
  vpc_id      = aws_vpc.main.id

  ingress {
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [aws_security_group.lambda.id]
  }
}
```

```hcl
# deploy/aws/modules/networking/variables.tf
variable "name_prefix" {
  type = string
}

variable "vpc_cidr" {
  type    = string
  default = "10.42.0.0/16"
}

variable "availability_zones" {
  type = list(string)
}
```

```hcl
# deploy/aws/modules/networking/outputs.tf
output "vpc_id" {
  value = aws_vpc.main.id
}

output "private_subnet_ids" {
  value = aws_subnet.private[*].id
}

output "public_subnet_ids" {
  value = aws_subnet.public[*].id
}

output "lambda_security_group_id" {
  value = aws_security_group.lambda.id
}

output "database_security_group_id" {
  value = aws_security_group.database.id
}
```

- [ ] **Step 3: Validate**

```bash
cd deploy/aws/bootstrap && terraform init && terraform validate && cd -
cd deploy/aws/modules/networking && terraform init -backend=false && terraform validate && cd -
```

Expected: both report `Success!`. This costs nothing and creates nothing — `validate` only checks
syntax and internal consistency.

- [ ] **Step 4: Commit**

```bash
git add deploy/aws/bootstrap deploy/aws/modules/networking
git commit -m "feat(deploy): add Terraform state backend and networking module"
```

---

## Task 5: Terraform database module — RDS PostgreSQL with a least-privilege app role

**Operational note this task's design depends on:** creating `snaglist_app` as a role distinct from
the RDS master user (07-deployment-aws.md's explicit requirement) needs an actual SQL connection to
run `CREATE ROLE`/`GRANT` — the AWS provider alone can't do that, so this module also uses the
`cyrilgdn/postgresql` provider, which connects using the master credentials. Since the database sits
in a private subnet with no public access (by design), **whoever runs `terraform apply` for this
module needs network access into the VPC** — an SSM Session Manager port-forward, a VPN, or a
runner already inside the VPC. This is normal for private-subnet Terraform-managed Postgres; it is
not a gap introduced here, but it is a real precondition Task 9 must satisfy, not skip past.

**Files:**
- Create: `deploy/aws/modules/database/main.tf`
- Create: `deploy/aws/modules/database/variables.tf`
- Create: `deploy/aws/modules/database/outputs.tf`

**Interfaces:**
- Consumes: `private_subnet_ids`, `database_security_group_id` (Task 4).
- Produces: `app_secret_arn` (the app role's Secrets Manager entry) and `master_secret_arn` —
  consumed by Task 6's config module for the Lambda environment's Secrets Manager references, and
  by Task 8's root module for wiring.

- [ ] **Step 1: Write the module**

```hcl
# deploy/aws/modules/database/main.tf
terraform {
  required_providers {
    postgresql = { source = "cyrilgdn/postgresql", version = "~> 1.21" }
  }
}

resource "aws_db_subnet_group" "main" {
  name       = "${var.name_prefix}-db-subnets"
  subnet_ids = var.private_subnet_ids
}

resource "random_password" "master" {
  length  = 32
  special = false
}

resource "aws_db_instance" "main" {
  identifier              = "${var.name_prefix}-postgres"
  engine                  = "postgres"
  engine_version          = "16"
  instance_class          = var.instance_class
  allocated_storage       = 20
  storage_encrypted       = true
  db_name                 = "snaglist"
  username                = "snaglist_master"
  password                = random_password.master.result
  db_subnet_group_name    = aws_db_subnet_group.main.name
  vpc_security_group_ids  = [var.database_security_group_id]
  deletion_protection     = var.deletion_protection
  backup_retention_period = 7
  skip_final_snapshot     = !var.deletion_protection
}

resource "aws_secretsmanager_secret" "db_master" {
  name = "${var.name_prefix}/db/master"
}

resource "aws_secretsmanager_secret_version" "db_master" {
  secret_id     = aws_secretsmanager_secret.db_master.id
  secret_string = jsonencode({ username = aws_db_instance.main.username, password = random_password.master.result })
}

resource "random_password" "app" {
  length  = 32
  special = false
}

resource "aws_secretsmanager_secret" "db_app" {
  name = "${var.name_prefix}/db/app"
}

resource "aws_secretsmanager_secret_version" "db_app" {
  secret_id     = aws_secretsmanager_secret.db_app.id
  secret_string = jsonencode({ username = "snaglist_app", password = random_password.app.result })
}

provider "postgresql" {
  host      = aws_db_instance.main.address
  port      = 5432
  username  = aws_db_instance.main.username
  password  = random_password.master.result
  sslmode   = "require"
  superuser = false
}

# The application role: no table-creation/schema-ownership rights, no ability to alter the schema
# EF Core migrations own — DML only. The migration runner (SnagList.SeedData, running with the
# master credentials in Task 7) is the only thing that ever runs migrations.
resource "postgresql_role" "app" {
  name     = "snaglist_app"
  login    = true
  password = random_password.app.result
}

resource "postgresql_grant" "app_schema_usage" {
  database    = aws_db_instance.main.db_name
  role        = postgresql_role.app.name
  schema      = "public"
  object_type = "schema"
  privileges  = ["USAGE"]
}

resource "postgresql_grant" "app_table_dml" {
  database    = aws_db_instance.main.db_name
  role        = postgresql_role.app.name
  schema      = "public"
  object_type = "table"
  privileges  = ["SELECT", "INSERT", "UPDATE", "DELETE"]
}
```

- [ ] **Step 2: Write `variables.tf` and `outputs.tf`**

```hcl
# deploy/aws/modules/database/variables.tf
variable "name_prefix" {
  type = string
}

variable "private_subnet_ids" {
  type = list(string)
}

variable "database_security_group_id" {
  type = string
}

variable "instance_class" {
  type    = string
  default = "db.t4g.micro"
}

variable "deletion_protection" {
  type    = bool
  default = true
}
```

```hcl
# deploy/aws/modules/database/outputs.tf
output "address" {
  value = aws_db_instance.main.address
}

output "db_name" {
  value = aws_db_instance.main.db_name
}

output "master_secret_arn" {
  value = aws_secretsmanager_secret.db_master.arn
}

output "app_secret_arn" {
  value = aws_secretsmanager_secret.db_app.arn
}
```

- [ ] **Step 3: Validate**

```bash
cd deploy/aws/modules/database && terraform init -backend=false && terraform validate && cd -
```

Expected: `Success!`.

- [ ] **Step 4: Commit**

```bash
git add deploy/aws/modules/database
git commit -m "feat(deploy): add Terraform database module with a least-privilege app role"
```

---

## Task 6: Secrets Manager connection resolution, SSM config, and SES

**Design correction caught while writing this task:** 07-deployment-aws.md says "Secrets Manager
(token signing key, DB creds)" — but SnagList has no token-signing key at all. Unlike JointBooking
(the pattern that sentence was carried over from), SnagList never issues or signs its own tokens;
Entra ID does, and the app only validates them against Entra's JWKS. There is nothing here to sign.
This task implements the DB-credentials half only, and the spec's mention of a signing key is
simply wrong for this app — noted here rather than silently ignored.

**The actual problem this task solves:** the Global Constraint that no secret reaches a Lambda
environment variable in plaintext means `ConnectionStrings:SnagList` can't just *be* the connection
string the way it is in local docker and home-lab (Plans 1 and 4) — Terraform interpolating a
Secrets Manager value straight into a Lambda's `environment` block would still leave it sitting in
plaintext in that function's configuration, visible to anyone with `lambda:GetFunctionConfiguration`.
Instead, the app resolves it itself, at startup, via the Secrets Manager API using its own IAM role
— the environment only ever carries the secret's ARN (not sensitive) plus the non-secret host/port/
database name.

**Files:**
- Create: `src/SnagList.Infrastructure/Configuration/SecretsManagerConnectionStringResolver.cs`
- Test: `tests/SnagList.Infrastructure.Tests/Configuration/SecretsManagerConnectionStringResolverTests.cs`
- Modify: `src/SnagList.Api/Program.cs` — resolve the connection string before
  `AddSnagListInfrastructure`, when `Database:SecretArn` is configured
- Modify: `src/SnagList.Mcp/Program.cs` — same
- Create: `deploy/aws/modules/config/main.tf`
- Create: `deploy/aws/modules/config/variables.tf`
- Create: `deploy/aws/modules/config/outputs.tf`
- Create: `deploy/aws/modules/email/main.tf`
- Create: `deploy/aws/modules/email/variables.tf`
- Create: `deploy/aws/modules/email/outputs.tf`

**Interfaces:**
- Produces: `SecretsManagerConnectionStringResolver.ResolveConnectionStringAsync(secretArn, host,
  port, databaseName, ct) -> string`; the `config`/`email` Terraform modules' outputs, consumed by
  Task 7's Lambda environment block.

- [ ] **Step 1: Add the package**

```bash
dotnet add src/SnagList.Infrastructure package AWSSDK.SecretsManager
dotnet add src/SnagList.Api package AWSSDK.SecretsManager
dotnet add src/SnagList.Mcp package AWSSDK.SecretsManager
```

- [ ] **Step 2: Write the failing test**

```csharp
namespace SnagList.Infrastructure.Tests.Configuration;

using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using NSubstitute;
using SnagList.Infrastructure.Configuration;
using Xunit;

public class SecretsManagerConnectionStringResolverTests
{
    [Fact]
    public async Task ResolveConnectionStringAsync_builds_the_connection_string_from_the_secret()
    {
        var secretsManager = Substitute.For<IAmazonSecretsManager>();
        secretsManager.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetSecretValueResponse { SecretString = """{"Username":"snaglist_app","Password":"s3cr3t"}""" });
        var resolver = new SecretsManagerConnectionStringResolver(secretsManager);

        var connectionString = await resolver.ResolveConnectionStringAsync(
            "arn:aws:secretsmanager:...", "db.example.com", 5432, "snaglist", default);

        Assert.Contains("Host=db.example.com", connectionString);
        Assert.Contains("Port=5432", connectionString);
        Assert.Contains("Database=snaglist", connectionString);
        Assert.Contains("Username=snaglist_app", connectionString);
        Assert.Contains("Password=s3cr3t", connectionString);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter SecretsManagerConnectionStringResolverTests`
Expected: FAIL — `SecretsManagerConnectionStringResolver` does not exist.

- [ ] **Step 4: Implement**

```csharp
namespace SnagList.Infrastructure.Configuration;

using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

public sealed class SecretsManagerConnectionStringResolver(IAmazonSecretsManager secretsManager)
{
    private sealed record DbCredentials(string Username, string Password);

    public async Task<string> ResolveConnectionStringAsync(
        string secretArn, string host, int port, string databaseName, CancellationToken ct)
    {
        var response = await secretsManager.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretArn }, ct);
        var credentials = JsonSerializer.Deserialize<DbCredentials>(response.SecretString)
            ?? throw new InvalidOperationException($"Secret '{secretArn}' did not contain the expected username/password shape.");
        return $"Host={host};Port={port};Database={databaseName};Username={credentials.Username};Password={credentials.Password}";
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter SecretsManagerConnectionStringResolverTests`
Expected: PASS.

- [ ] **Step 6: Wire it into `Program.cs` (both `Api` and `Mcp`)**

Add, before `builder.Services.AddSnagListInfrastructure(builder.Configuration);`:

```csharp
if (builder.Configuration["Database:SecretArn"] is { } dbSecretArn)
{
    var resolver = new SecretsManagerConnectionStringResolver(new AmazonSecretsManagerClient());
    var connectionString = await resolver.ResolveConnectionStringAsync(
        dbSecretArn,
        builder.Configuration["Database:Host"] ?? throw new InvalidOperationException("Database:Host is required."),
        int.Parse(builder.Configuration["Database:Port"] ?? "5432"),
        builder.Configuration["Database:Name"] ?? throw new InvalidOperationException("Database:Name is required."),
        default);
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SnagList"] = connectionString });
}
```

Add `using Amazon.SecretsManager;` and `using SnagList.Infrastructure.Configuration;` to both
files. Since this introduces the file's first `await`, change `app.Run();` to `await
app.RunAsync();` at the bottom of both files for consistency (top-level statements already support
`await`, so this alone doesn't require any other change). Local docker and home-lab never set
`Database:SecretArn`, so this block is skipped entirely for them — `ConnectionStrings:SnagList`
keeps coming from the environment variable directly, unchanged.

- [ ] **Step 7: Run the Api and Mcp test suites to confirm no regressions**

Run: `dotnet test tests/SnagList.Api.Tests`
Run: `dotnet test tests/SnagList.Mcp.Tests`
Expected: PASS — every existing test's config omits `Database:SecretArn`, so this new branch never
activates for them.

- [ ] **Step 8: Write the `config` Terraform module**

Holds the non-secret application configuration as SSM parameters, and the IAM policy document
granting the Lambda role read access to exactly the one DB secret and these parameters — nothing
broader.

```hcl
# deploy/aws/modules/config/main.tf
resource "aws_ssm_parameter" "storage_bucket_name" {
  name  = "/${var.name_prefix}/Storage/BucketName"
  type  = "String"
  value = var.storage_bucket_name
}

resource "aws_ssm_parameter" "notifications_maintenance_email" {
  name  = "/${var.name_prefix}/Notifications/MaintenanceTeamEmail"
  type  = "String"
  value = var.maintenance_team_email
}

resource "aws_ssm_parameter" "auth_entraid_tenant_id" {
  name  = "/${var.name_prefix}/Auth/EntraId/TenantId"
  type  = "String"
  value = var.entra_tenant_id
}

resource "aws_ssm_parameter" "auth_entraid_audience" {
  name  = "/${var.name_prefix}/Auth/EntraId/Audience"
  type  = "String"
  value = var.entra_audience
}

data "aws_iam_policy_document" "lambda_config_access" {
  statement {
    sid       = "ReadDbAppSecret"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [var.db_app_secret_arn]
  }
  statement {
    sid     = "ReadSsmConfig"
    actions = ["ssm:GetParameter", "ssm:GetParameters"]
    resources = [
      aws_ssm_parameter.storage_bucket_name.arn,
      aws_ssm_parameter.notifications_maintenance_email.arn,
      aws_ssm_parameter.auth_entraid_tenant_id.arn,
      aws_ssm_parameter.auth_entraid_audience.arn,
    ]
  }
}
```

```hcl
# deploy/aws/modules/config/variables.tf
variable "name_prefix" {
  type = string
}
variable "storage_bucket_name" {
  type = string
}
variable "maintenance_team_email" {
  type = string
}
variable "entra_tenant_id" {
  type = string
}
variable "entra_audience" {
  type = string
}
variable "db_app_secret_arn" {
  type = string
}
```

```hcl
# deploy/aws/modules/config/outputs.tf
output "lambda_config_access_policy_json" {
  value = data.aws_iam_policy_document.lambda_config_access.json
}

output "ssm_parameter_names" {
  value = {
    storage_bucket_name              = aws_ssm_parameter.storage_bucket_name.name
    notifications_maintenance_email  = aws_ssm_parameter.notifications_maintenance_email.name
    auth_entraid_tenant_id           = aws_ssm_parameter.auth_entraid_tenant_id.name
    auth_entraid_audience            = aws_ssm_parameter.auth_entraid_audience.name
  }
}
```

- [ ] **Step 9: Write the `email` Terraform module**

```hcl
# deploy/aws/modules/email/main.tf
resource "aws_sesv2_email_identity" "domain" {
  email_identity = var.domain_name
}

data "aws_iam_policy_document" "lambda_ses_send" {
  statement {
    sid       = "SendEmailFromVerifiedDomain"
    actions   = ["ses:SendEmail"]
    resources = [aws_sesv2_email_identity.domain.arn]
  }
}
```

```hcl
# deploy/aws/modules/email/variables.tf
variable "domain_name" {
  type = string
}
```

```hcl
# deploy/aws/modules/email/outputs.tf
output "dkim_tokens" {
  value = aws_sesv2_email_identity.domain.dkim_signing_attributes[0].tokens
}

output "lambda_ses_send_policy_json" {
  value = data.aws_iam_policy_document.lambda_ses_send.json
}
```

`dkim_tokens` must be published as CNAME records in the domain's real DNS before SES will actually
accept mail from it — exactly the same "this repo can specify the resource but not the DNS it
doesn't own" boundary as the home-lab plan's realm-export hostname placeholder (Plan 4, Task 3).
That's a manual step for whoever owns `var.domain_name`'s DNS, not something Terraform here can do.

- [ ] **Step 10: Validate**

```bash
cd deploy/aws/modules/config && terraform init -backend=false && terraform validate && cd -
cd deploy/aws/modules/email && terraform init -backend=false && terraform validate && cd -
```

Expected: both `Success!`.

- [ ] **Step 11: Commit**

```bash
git add src/SnagList.Infrastructure/Configuration src/SnagList.Api/Program.cs src/SnagList.Mcp/Program.cs \
  tests/SnagList.Infrastructure.Tests/Configuration deploy/aws/modules/config deploy/aws/modules/email
git commit -m "feat(deploy): resolve DB credentials from Secrets Manager; add config and email Terraform modules"
```

---

## Task 7: ECR, image promotion, and the `compute` module — Lambda + two API Gateways

**Real constraint this task exists because of:** Lambda's container-image support requires the
image to live in *Amazon ECR* specifically — not GHCR, not any other registry. Plan 4's
`publish-images.yml` already builds and pushes to GHCR (reused as-is by local docker and home-lab);
rather than rebuild for AWS and risk the image drifting from what GHCR already proved out, this task
adds a second, separate step that copies the already-built image from GHCR into ECR by digest — a
promotion, not a rebuild, keeping Plan 1's "one image, everywhere" constraint intact.

**Files:**
- Create: `.github/workflows/promote-image-to-ecr.yml`
- Create: `deploy/aws/modules/compute/main.tf`
- Create: `deploy/aws/modules/compute/variables.tf`
- Create: `deploy/aws/modules/compute/outputs.tf`

**Interfaces:**
- Consumes: every output from Tasks 4–6 (`private_subnet_ids`, `lambda_security_group_id`,
  `db_app_secret_arn`, `address`/`db_name`, `lambda_config_access_policy_json`,
  `lambda_ses_send_policy_json`).
- Produces: `api_invoke_url`/`mcp_invoke_url` — consumed by Task 8's root module output and Task 9's
  manual verification.

- [ ] **Step 1: Write the image-promotion workflow**

Assumes an IAM role (`github_actions_deploy`, created by this task's Terraform below) trusted for
GitHub Actions OIDC — no long-lived AWS access keys stored as GitHub secrets.

```yaml
name: Promote image to ECR

on:
  workflow_dispatch:
    inputs:
      image_tag:
        description: "GHCR tag to promote (a commit SHA from a previous publish-images.yml run)"
        required: true
      aws_region:
        required: true
        default: eu-west-1

permissions:
  contents: read
  packages: read
  id-token: write

jobs:
  promote:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        service: [api, mcp]
    steps:
      - uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - uses: aws-actions/configure-aws-credentials@v4
        with:
          role-to-assume: ${{ secrets.AWS_DEPLOY_ROLE_ARN }}
          aws-region: ${{ inputs.aws_region }}

      - uses: aws-actions/amazon-ecr-login@v2
        id: ecr-login

      - name: Pull from GHCR, tag, and push to ECR
        run: |
          docker pull ghcr.io/${{ github.repository_owner }}/snaglist-${{ matrix.service }}:${{ inputs.image_tag }}
          docker tag ghcr.io/${{ github.repository_owner }}/snaglist-${{ matrix.service }}:${{ inputs.image_tag }} \
            ${{ steps.ecr-login.outputs.registry }}/snaglist-${{ matrix.service }}:${{ inputs.image_tag }}
          docker push ${{ steps.ecr-login.outputs.registry }}/snaglist-${{ matrix.service }}:${{ inputs.image_tag }}
```

`AWS_DEPLOY_ROLE_ARN` (a repository secret holding the ARN this task's Terraform creates, not a
credential itself) is set once, manually, after Step 2 first applies — see Task 9.

- [ ] **Step 2: Write the `compute` module**

```hcl
# deploy/aws/modules/compute/main.tf
resource "aws_ecr_repository" "api" {
  name                 = "${var.name_prefix}-api"
  image_tag_mutability = "IMMUTABLE"
  image_scanning_configuration { scan_on_push = true }
}

resource "aws_ecr_repository" "mcp" {
  name                 = "${var.name_prefix}-mcp"
  image_tag_mutability = "IMMUTABLE"
  image_scanning_configuration { scan_on_push = true }
}

# The GitHub OIDC provider is a once-per-AWS-account resource — assumed to already exist rather
# than created here, so a second repository in the same account doesn't collide trying to create it.
data "aws_iam_openid_connect_provider" "github" {
  url = "https://token.actions.githubusercontent.com"
}

resource "aws_iam_role" "github_actions_deploy" {
  name = "${var.name_prefix}-github-actions-deploy"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Federated = data.aws_iam_openid_connect_provider.github.arn }
      Action    = "sts:AssumeRoleWithWebIdentity"
      Condition = {
        StringEquals = { "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com" }
        StringLike   = { "token.actions.githubusercontent.com:sub" = "repo:${var.github_repository}:*" }
      }
    }]
  })
}

resource "aws_iam_role_policy" "github_actions_ecr_push" {
  name = "ecr-push"
  role = aws_iam_role.github_actions_deploy.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      { Effect = "Allow", Action = "ecr:GetAuthorizationToken", Resource = "*" },
      {
        Effect = "Allow"
        Action = ["ecr:BatchCheckLayerAvailability", "ecr:PutImage", "ecr:InitiateLayerUpload", "ecr:UploadLayerPart", "ecr:CompleteLayerUpload"]
        Resource = [aws_ecr_repository.api.arn, aws_ecr_repository.mcp.arn]
      },
    ]
  })
}

resource "aws_iam_role" "lambda_execution" {
  name = "${var.name_prefix}-lambda-execution"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{ Effect = "Allow", Principal = { Service = "lambda.amazonaws.com" }, Action = "sts:AssumeRole" }]
  })
}

resource "aws_iam_role_policy_attachment" "lambda_basic" {
  role       = aws_iam_role.lambda_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

resource "aws_iam_role_policy" "lambda_config_access" {
  name   = "config-access"
  role   = aws_iam_role.lambda_execution.name
  policy = var.lambda_config_access_policy_json
}

resource "aws_iam_role_policy" "lambda_ses_send" {
  name   = "ses-send"
  role   = aws_iam_role.lambda_execution.name
  policy = var.lambda_ses_send_policy_json
}

resource "aws_iam_role_policy" "lambda_s3_photos" {
  name = "s3-photos"
  role = aws_iam_role.lambda_execution.name
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"]
      Resource = "${var.photos_bucket_arn}/*"
    }]
  })
}

locals {
  common_environment = {
    Database__SecretArn                 = var.db_app_secret_arn
    Database__Host                      = var.db_address
    Database__Port                      = "5432"
    Database__Name                      = var.db_name
    Storage__Provider                   = "S3"
    Storage__BucketName                 = var.photos_bucket_name
    Email__Provider                     = "Ses"
    Email__FromAddress                  = var.email_from_address
    Notifications__MaintenanceTeamEmail = var.maintenance_team_email
    Auth__Provider                      = "EntraId"
    Auth__EntraId__TenantId             = var.entra_tenant_id
    Auth__EntraId__Audience             = var.entra_audience
  }
}

resource "aws_lambda_function" "api" {
  function_name = "${var.name_prefix}-api"
  role          = aws_iam_role.lambda_execution.arn
  package_type  = "Image"
  image_uri     = "${aws_ecr_repository.api.repository_url}:${var.image_tag}"
  timeout       = 30
  memory_size   = 512

  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [var.lambda_security_group_id]
  }

  environment {
    variables = local.common_environment
  }
}

resource "aws_lambda_function" "mcp" {
  function_name = "${var.name_prefix}-mcp"
  role          = aws_iam_role.lambda_execution.arn
  package_type  = "Image"
  image_uri     = "${aws_ecr_repository.mcp.repository_url}:${var.image_tag}"
  timeout       = 30
  memory_size   = 512

  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [var.lambda_security_group_id]
  }

  environment {
    variables = local.common_environment
  }
}

resource "aws_apigatewayv2_api" "api" {
  name          = "${var.name_prefix}-api"
  protocol_type = "HTTP"
}

resource "aws_apigatewayv2_integration" "api" {
  api_id                 = aws_apigatewayv2_api.api.id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.api.invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "api_default" {
  api_id    = aws_apigatewayv2_api.api.id
  route_key = "$default"
  target    = "integrations/${aws_apigatewayv2_integration.api.id}"
}

resource "aws_apigatewayv2_stage" "api" {
  api_id      = aws_apigatewayv2_api.api.id
  name        = "$default"
  auto_deploy = true
}

resource "aws_lambda_permission" "api_gateway" {
  statement_id  = "AllowAPIGatewayInvoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.api.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.api.execution_arn}/*/*"
}

resource "aws_apigatewayv2_api" "mcp" {
  name          = "${var.name_prefix}-mcp"
  protocol_type = "HTTP"
}

resource "aws_apigatewayv2_integration" "mcp" {
  api_id                 = aws_apigatewayv2_api.mcp.id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.mcp.invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "mcp_default" {
  api_id    = aws_apigatewayv2_api.mcp.id
  route_key = "$default"
  target    = "integrations/${aws_apigatewayv2_integration.mcp.id}"
}

resource "aws_apigatewayv2_stage" "mcp" {
  api_id      = aws_apigatewayv2_api.mcp.id
  name        = "$default"
  auto_deploy = true
}

resource "aws_lambda_permission" "mcp_api_gateway" {
  statement_id  = "AllowAPIGatewayInvoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.mcp.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.mcp.execution_arn}/*/*"
}
```

```hcl
# deploy/aws/modules/compute/variables.tf
variable "name_prefix" { type = string }
variable "github_repository" { type = string } # e.g. "james-mitchell-ba/SnagList"
variable "image_tag" { type = string }
variable "private_subnet_ids" { type = list(string) }
variable "lambda_security_group_id" { type = string }
variable "db_app_secret_arn" { type = string }
variable "db_address" { type = string }
variable "db_name" { type = string }
variable "photos_bucket_arn" { type = string }
variable "photos_bucket_name" { type = string }
variable "email_from_address" { type = string }
variable "maintenance_team_email" { type = string }
variable "entra_tenant_id" { type = string }
variable "entra_audience" { type = string }
variable "lambda_config_access_policy_json" { type = string }
variable "lambda_ses_send_policy_json" { type = string }
```

```hcl
# deploy/aws/modules/compute/outputs.tf
output "api_invoke_url" {
  value = aws_apigatewayv2_stage.api.invoke_url
}
output "mcp_invoke_url" {
  value = aws_apigatewayv2_stage.mcp.invoke_url
}
output "github_actions_deploy_role_arn" {
  value = aws_iam_role.github_actions_deploy.arn
}
output "ecr_api_repository_url" {
  value = aws_ecr_repository.api.repository_url
}
output "ecr_mcp_repository_url" {
  value = aws_ecr_repository.mcp.repository_url
}
```

- [ ] **Step 3: Validate**

```bash
cd deploy/aws/modules/compute && terraform init -backend=false && terraform validate && cd -
```

Expected: `Success!`.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/promote-image-to-ecr.yml deploy/aws/modules/compute
git commit -m "feat(deploy): add ECR/Lambda/API Gateway compute module and image promotion workflow"
```

---

## Task 8: Static-site module and the root Terraform configuration

**Files:**
- Create: `deploy/aws/modules/static-site/main.tf`
- Create: `deploy/aws/modules/static-site/variables.tf`
- Create: `deploy/aws/modules/static-site/outputs.tf`
- Create: `deploy/aws/main.tf`
- Create: `deploy/aws/variables.tf`
- Create: `deploy/aws/outputs.tf`
- Create: `deploy/aws/environments/example.tfvars`
- Create: `.github/workflows/deploy-web-static-site.yml`

**Interfaces:** none — this is the deployment target itself, wiring together every module from
Tasks 4–7.

- [ ] **Step 1: Write the `static-site` module**

```hcl
# deploy/aws/modules/static-site/main.tf
resource "aws_s3_bucket" "web" {
  bucket = "${var.name_prefix}-web"
}

resource "aws_s3_bucket_public_access_block" "web" {
  bucket                  = aws_s3_bucket.web.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_cloudfront_origin_access_control" "web" {
  name                              = "${var.name_prefix}-web-oac"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

resource "aws_cloudfront_distribution" "web" {
  enabled             = true
  default_root_object = "index.html"

  origin {
    domain_name              = aws_s3_bucket.web.bucket_regional_domain_name
    origin_id                = "web-s3-origin"
    origin_access_control_id = aws_cloudfront_origin_access_control.web.id
  }

  default_cache_behavior {
    target_origin_id       = "web-s3-origin"
    viewer_protocol_policy = "redirect-to-https"
    allowed_methods        = ["GET", "HEAD"]
    cached_methods         = ["GET", "HEAD"]
    forwarded_values {
      query_string = false
      cookies { forward = "none" }
    }
  }

  # Blazor WASM client-side routing: an unknown path (e.g. /snags/{id}) has no matching S3 object,
  # so CloudFront must serve index.html instead of a raw 404, letting the Blazor router take over.
  custom_error_response {
    error_code         = 404
    response_code      = 200
    response_page_path = "/index.html"
  }

  restrictions {
    geo_restriction { restriction_type = "none" }
  }

  viewer_certificate {
    cloudfront_default_certificate = true
  }
}

data "aws_iam_policy_document" "web_bucket_policy" {
  statement {
    sid       = "AllowCloudFrontRead"
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.web.arn}/*"]
    principals {
      type        = "Service"
      identifiers = ["cloudfront.amazonaws.com"]
    }
    condition {
      test     = "StringEquals"
      variable = "AWS:SourceArn"
      values   = [aws_cloudfront_distribution.web.arn]
    }
  }
}

resource "aws_s3_bucket_policy" "web" {
  bucket = aws_s3_bucket.web.id
  policy = data.aws_iam_policy_document.web_bucket_policy.json
}
```

```hcl
# deploy/aws/modules/static-site/variables.tf
variable "name_prefix" {
  type = string
}
```

```hcl
# deploy/aws/modules/static-site/outputs.tf
output "bucket_name" {
  value = aws_s3_bucket.web.bucket
}
output "cloudfront_distribution_id" {
  value = aws_cloudfront_distribution.web.id
}
output "cloudfront_domain_name" {
  value = aws_cloudfront_distribution.web.domain_name
}
```

- [ ] **Step 2: Write the root configuration**

```hcl
# deploy/aws/main.tf
terraform {
  required_version = ">= 1.9"
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 5.0" }
  }
  # bucket/key/region/dynamodb_table supplied via `-backend-config` at init time, from the
  # bootstrap module's own outputs (Task 4) — hardcoding them here would recreate the exact
  # chicken-and-egg problem the bootstrap module exists to avoid.
  backend "s3" {}
}

provider "aws" {
  region = var.aws_region
}

module "networking" {
  source             = "./modules/networking"
  name_prefix        = var.name_prefix
  availability_zones = var.availability_zones
}

module "database" {
  source                      = "./modules/database"
  name_prefix                 = var.name_prefix
  private_subnet_ids          = module.networking.private_subnet_ids
  database_security_group_id  = module.networking.database_security_group_id
  deletion_protection         = var.deletion_protection
}

resource "aws_s3_bucket" "photos" {
  bucket = "${var.name_prefix}-photos"
}

resource "aws_s3_bucket_server_side_encryption_configuration" "photos" {
  bucket = aws_s3_bucket.photos.id
  rule {
    apply_server_side_encryption_by_default { sse_algorithm = "AES256" }
  }
}

resource "aws_s3_bucket_public_access_block" "photos" {
  bucket                  = aws_s3_bucket.photos.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

module "email" {
  source      = "./modules/email"
  domain_name = var.email_domain_name
}

module "config" {
  source                 = "./modules/config"
  name_prefix            = var.name_prefix
  storage_bucket_name    = aws_s3_bucket.photos.bucket
  maintenance_team_email = var.maintenance_team_email
  entra_tenant_id        = var.entra_tenant_id
  entra_audience         = var.entra_audience
  db_app_secret_arn      = module.database.app_secret_arn
}

module "compute" {
  source                            = "./modules/compute"
  name_prefix                       = var.name_prefix
  github_repository                 = var.github_repository
  image_tag                         = var.image_tag
  private_subnet_ids                = module.networking.private_subnet_ids
  lambda_security_group_id          = module.networking.lambda_security_group_id
  db_app_secret_arn                 = module.database.app_secret_arn
  db_address                        = module.database.address
  db_name                           = module.database.db_name
  photos_bucket_arn                 = aws_s3_bucket.photos.arn
  photos_bucket_name                = aws_s3_bucket.photos.bucket
  email_from_address                = var.email_from_address
  maintenance_team_email            = var.maintenance_team_email
  entra_tenant_id                   = var.entra_tenant_id
  entra_audience                    = var.entra_audience
  lambda_config_access_policy_json  = module.config.lambda_config_access_policy_json
  lambda_ses_send_policy_json       = module.email.lambda_ses_send_policy_json
}

module "static_site" {
  source      = "./modules/static-site"
  name_prefix = var.name_prefix
}
```

```hcl
# deploy/aws/variables.tf
variable "aws_region" {
  type = string
}
variable "name_prefix" {
  type    = string
  default = "snaglist"
}
variable "availability_zones" {
  type = list(string)
}
variable "deletion_protection" {
  type    = bool
  default = true
}
variable "github_repository" {
  type = string
}
variable "image_tag" {
  type = string
}
variable "email_domain_name" {
  type = string
}
variable "email_from_address" {
  type = string
}
variable "maintenance_team_email" {
  type = string
}
variable "entra_tenant_id" {
  type = string
}
variable "entra_audience" {
  type    = string
  default = "snaglist-api"
}
```

```hcl
# deploy/aws/outputs.tf
output "api_invoke_url" {
  value = module.compute.api_invoke_url
}
output "mcp_invoke_url" {
  value = module.compute.mcp_invoke_url
}
output "web_bucket_name" {
  value = module.static_site.bucket_name
}
output "web_cloudfront_distribution_id" {
  value = module.static_site.cloudfront_distribution_id
}
output "web_cloudfront_domain_name" {
  value = module.static_site.cloudfront_domain_name
}
output "github_actions_deploy_role_arn" {
  value = module.compute.github_actions_deploy_role_arn
}
output "ses_dkim_tokens" {
  value = module.email.dkim_tokens
}
```

```hcl
# deploy/aws/environments/example.tfvars — copy, fill in, never commit the filled-in copy.
aws_region             = "eu-west-1"
name_prefix            = "snaglist"
availability_zones     = ["eu-west-1a", "eu-west-1b"]
deletion_protection    = true
github_repository      = "james-mitchell-ba/SnagList"
image_tag              = "REPLACE_WITH_A_PROMOTED_IMAGE_TAG"
email_domain_name      = "snaglist.example.com"
email_from_address     = "snaglist@snaglist.example.com"
maintenance_team_email = "maintenance@snaglist.example.com"
entra_tenant_id        = "REPLACE_WITH_YOUR_ENTRA_TENANT_ID"
```

- [ ] **Step 3: Write the web static-site deploy workflow**

Terraform provisions the S3 bucket and CloudFront distribution (Step 1) but deliberately doesn't
upload files or invalidate the cache — that's an application deployment concern, not infrastructure
provisioning, and belongs in CI alongside the image promotion, not in `terraform apply`.

```yaml
name: Deploy web static site

on:
  workflow_dispatch:
    inputs:
      aws_region:
        required: true
        default: eu-west-1
      s3_bucket:
        description: "Terraform output web_bucket_name"
        required: true
      cloudfront_distribution_id:
        description: "Terraform output web_cloudfront_distribution_id"
        required: true

permissions:
  contents: read
  id-token: write

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"

      - run: dotnet publish src/SnagList.Web/SnagList.Web.csproj -c Release -o publish-output

      - uses: aws-actions/configure-aws-credentials@v4
        with:
          role-to-assume: ${{ secrets.AWS_DEPLOY_ROLE_ARN }}
          aws-region: ${{ inputs.aws_region }}

      - run: aws s3 sync publish-output/wwwroot s3://${{ inputs.s3_bucket }} --delete

      - run: aws cloudfront create-invalidation --distribution-id ${{ inputs.cloudfront_distribution_id }} --paths "/*"
```

- [ ] **Step 4: Validate**

```bash
cd deploy/aws/modules/static-site && terraform init -backend=false && terraform validate && cd -
cd deploy/aws && terraform init -backend=false && terraform validate && cd -
```

Expected: both `Success!`. `terraform init -backend=false` skips configuring the real S3 backend
for this validation-only check — Task 9's actual `terraform init` uses the real one.

- [ ] **Step 5: Commit**

```bash
git add deploy/aws/modules/static-site deploy/aws/main.tf deploy/aws/variables.tf deploy/aws/outputs.tf \
  deploy/aws/environments .github/workflows/deploy-web-static-site.yml
git commit -m "feat(deploy): add static-site module and the root AWS Terraform configuration"
```

---

## Task 9: Manual verification against a real AWS account

**This task costs real money and needs a real AWS account, a real Entra ID tenant, and a domain
you control the DNS for.** Everything through Task 8 was `validate`/`plan` only — free, and touches
nothing. Nothing below is optional-but-skippable the way some earlier plans' manual steps were:
this is genuinely the only way to prove Terraform's plan actually becomes working infrastructure,
since nothing in this repo can simulate RDS, Lambda, API Gateway, CloudFront, or a real Entra tenant
the way Testcontainers stood in for Postgres, MinIO, and Keycloak elsewhere.

- [ ] **Step 1: Register an Entra ID app and note its tenant ID and an audience/App ID URI**

In the Entra admin center: register an application, note the tenant ID, and expose an API with an
App ID URI (this becomes `entra_audience` — e.g. `api://snaglist-api`). Assign the `Staff` and
`Maintenance` app roles (matching `StaffRole`, Plan 1 Task 4) and add the `staff_id` optional claim.

- [ ] **Step 2: Apply the bootstrap module**

```bash
cd deploy/aws/bootstrap
terraform init
terraform apply -var="aws_region=eu-west-1" -var="state_bucket_name=snaglist-terraform-state-<your-account-id>"
```

Note the `state_bucket_name`/`lock_table_name` outputs.

- [ ] **Step 3: Init and plan the root configuration**

```bash
cd deploy/aws
terraform init \
  -backend-config="bucket=<state_bucket_name from Step 2>" \
  -backend-config="key=snaglist/terraform.tfstate" \
  -backend-config="region=eu-west-1" \
  -backend-config="dynamodb_table=<lock_table_name from Step 2>"

cp environments/example.tfvars environments/production.tfvars
# Fill in real values: entra_tenant_id, entra_audience, email_domain_name (a domain you control
# DNS for), github_repository, and a real image_tag once Step 5 has produced one.

terraform plan -var-file=environments/production.tfvars
```

Expected: a plan showing every resource across networking, database, config, email, compute
(without a real `image_tag` yet, this will fail on the Lambda resources — that's expected; apply
networking/database/config/email first with `-target`, or accept the Lambda failure and re-plan
after Step 5).

- [ ] **Step 4: Apply networking, database, config, and email first**

```bash
terraform apply -var-file=environments/production.tfvars \
  -target=module.networking -target=module.database -target=module.config -target=module.email
```

Expected: succeeds. Note this created a real RDS instance (billing starts now) and a real NAT
gateway (also billing).

- [ ] **Step 5: Promote an image and finish applying**

Set the `AWS_DEPLOY_ROLE_ARN` repository secret to the `github_actions_deploy_role_arn` Terraform
output (available after `module.compute` first plans — run `terraform apply -target=module.compute`
once to create the IAM role itself, using any placeholder `image_tag`, then set the secret). Then:

```bash
gh workflow run promote-image-to-ecr.yml -f image_tag=<a commit SHA from a recent publish-images.yml run>
```

Update `environments/production.tfvars`'s `image_tag` to the promoted tag, then:

```bash
terraform apply -var-file=environments/production.tfvars
```

Expected: succeeds — Lambda functions now reference a real ECR image; API Gateway, the photos S3
bucket, and the CloudFront distribution are all created.

- [ ] **Step 6: Deploy the web static site**

```bash
gh workflow run deploy-web-static-site.yml \
  -f s3_bucket=<web_bucket_name output> -f cloudfront_distribution_id=<web_cloudfront_distribution_id output>
```

- [ ] **Step 7: Add the SES DKIM records and run migrations/seed**

Add the three CNAME records from the `ses_dkim_tokens` output to `email_domain_name`'s real DNS
(per Task 6's note — this repo can't do this for you). Run the `SnagList.SeedData` image once
against the RDS instance (from a context with VPC access, per Task 5's note) to apply migrations.

- [ ] **Step 8: Smoke test against the real stack**

```bash
curl -s <api_invoke_url output>/health
# {"status":"healthy"}
```

Get a real Entra ID token for a user with the `Staff` app role assigned (via the Entra admin
center's token testing tools, or a real sign-in through `web`'s CloudFront URL once DNS/redirect
URIs are pointed at it), then:

```bash
curl -s <api_invoke_url output>/api/v1/me -H "Authorization: Bearer <token>"
```

Expected: the same identity/roles shape every other deployment's `/api/v1/me` returns, now coming
from Entra ID's claims via `SnagList.Api.Auth.EntraId` (Task 3) instead of Keycloak's.

- [ ] **Step 9: Commit any `.tfvars`-adjacent documentation updates**

Do not commit the filled-in `environments/production.tfvars` itself — it isn't secret by
construction (no plaintext credentials belong in it, per this plan's Global Constraints), but it's
still a specific deployment's configuration, not a template. If anything about the walkthrough
above needed correcting once run for real, update this task's steps and commit that.

```bash
git add docs/specs/2026-09-27-snaglist/implementation-plan-05-aws-deployment.md
git commit -m "docs: correct AWS deployment verification steps after a real run"
```

---

## Plan exit criteria

- `dotnet test` (every project, including Plans 1–4's) passes.
- `terraform validate` passes for every module and the root configuration.
- Task 9's real-AWS walkthrough succeeds: `api`/`mcp` respond as Lambda functions behind API
  Gateway, `web` serves from CloudFront, a real Entra ID token authenticates against
  `SnagList.Api.Auth.EntraId`, and a reported `Snag` triggers a real SES-delivered email.
- No secret exists in plaintext in any Lambda environment variable, Terraform state file committed
  to the repo (none should be — state lives only in the S3 backend), or `.tfvars` file under
  version control.
- No new domain concepts were introduced — this plan is pure deployment/infrastructure wiring
  (plus the `SesEmailSender`/`EntraIdAuthenticationExtensions` adapters, which are Infrastructure/
  Api-layer implementations of ports already modeled, not new domain concepts) — `docs/ontology.ttl`
  needs no changes.

**This was the last of the five plans.** All of 00-executive-summary.md's phased roadmap — core
domain/API, MCP, Blazor web, home-lab, and AWS — now has a written implementation plan.

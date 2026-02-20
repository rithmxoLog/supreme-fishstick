# PII Scrubbing Guide

**Last Updated**: 2026-02-09
**Applies To**: RithmTemplate v2.0+

---

## Overview

RithmTemplate automatically redacts PII (Personally Identifiable Information) from logs using Serilog enrichers. This ensures GDPR, CCPA, and other privacy compliance.

## What Gets Redacted

| Type | Example | Redacted |
|------|---------|----------|
| **Email** | `john@example.com` | `j***@e***.com` |
| **Phone** | `555-123-4567` | `***-***-****` |
| **Credit Card** | `4532 1234 5678 9010` | `**** **** **** ****` |
| **SSN/TIN** | `123-45-6789` | `***-**-****` |
| **IP Address** | `192.168.1.100` | `***.***.***.***` (optional) |

## Configuration

### appsettings.json
```json
{
  "Serilog": {
    "PiiRedaction": {
      "Enabled": true,
      "RedactEmails": true,
      "RedactPhones": true,
      "RedactCreditCards": true,
      "RedactSsn": true,
      "RedactIpAddresses": false,
      "CustomPatterns": [
        {
          "Pattern": "\\b\\d{10}\\b",
          "Replacement": "**********",
          "Description": "10-digit account numbers"
        }
      ]
    }
  }
}
```

### Environment-Specific

**Development** (`appsettings.Development.json`):
```json
{
  "Serilog": {
    "PiiRedaction": {
      "Enabled": false
    }
  }
}
```
*Disabled for easier debugging*

**Production** (`appsettings.Production.json`):
```json
{
  "Serilog": {
    "PiiRedaction": {
      "Enabled": true
    }
  }
}
```
*Always enabled for compliance*

## Usage

### Automatic Redaction

PII is automatically redacted from:
- Log message templates
- Exception messages
- Exception stack traces
- Log event properties

**Example**:
```csharp
// ✅ Logged with PII redaction
logger.LogInformation("User registered: {Email}", user.Email);
// Output: "User registered: j***@e***.com"

// ✅ Exception messages redacted
throw new Exception($"Duplicate email: {email}");
// Stack trace: "Duplicate email: j***@e***.com"
```

### Manual Redaction (if needed)

```csharp
using Rithm.Platform.Observability.Serilog;

var enricher = new PiiRedactionEnricher();
var redacted = enricher.ApplyRedaction("Contact john@example.com at 555-1234");
// Result: "Contact j***@e***.com at ***-****"
```

## Custom Patterns

Add custom regex patterns for domain-specific PII:

```json
{
  "CustomPatterns": [
    {
      "Pattern": "\\bACCT-\\d{8}\\b",
      "Replacement": "ACCT-********",
      "Description": "Internal account IDs"
    },
    {
      "Pattern": "\\b[A-Z]{2}\\d{6}\\b",
      "Replacement": "XX******",
      "Description": "Employee IDs"
    }
  ]
}
```

## Best Practices

### ✅ DO
- Keep PII redaction **enabled in production**
- Test custom patterns in development first
- Use structured logging with properties (not string interpolation)
- Document what PII your application handles

### ❌ DON'T
- Don't disable in production (compliance risk)
- Don't log raw user input without redaction
- Don't put PII in exception types/names
- Don't rely solely on redaction (minimize PII logging)

## Compliance

### GDPR Article 32
> "Implement appropriate technical measures to ensure security of processing"

PII redaction satisfies this requirement by preventing accidental PII exposure in logs.

### CCPA Section 1798.150
> "Implement reasonable security procedures"

Automated PII scrubbing demonstrates reasonable security measures.

## Performance

- **Overhead**: < 5ms per log event (regex-based)
- **Optimized**: Compiled regex patterns cached
- **Selective**: Only applies to string properties

## Troubleshooting

### Issue: PII Still Appearing in Logs

**Cause**: Redaction disabled or pattern mismatch

**Solution**:
1. Check `Serilog:PiiRedaction:Enabled = true`
2. Verify pattern matches your PII format
3. Test pattern with regex tool

### Issue: Too Much Redaction

**Cause**: Overly broad patterns

**Solution**: Make patterns more specific
```json
// ❌ Too broad
"Pattern": "\\d+"  // Matches ALL numbers

// ✅ Specific
"Pattern": "\\b\\d{10}\\b"  // Only 10-digit numbers
```

## Testing

```csharp
[Fact]
public void Should_Redact_Email_From_Log()
{
    var enricher = new PiiRedactionEnricher();
    var input = "User john@example.com registered";
    var output = enricher.ApplyRedaction(input);

    Assert.DoesNotContain("john@example.com", output);
    Assert.Contains("j***@e***.com", output);
}
```

---

**Implementation**: [`PiiRedactionEnricher.cs`](../backend/src/Rithm.Platform.Observability/Serilog/PiiRedactionEnricher.cs)

**Status**: Production-Ready

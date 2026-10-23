using System.ComponentModel.DataAnnotations;

namespace ToolShop.API.DTOs.Auth;

/// <summary>Returned by login instead of tokens when a second factor is needed.</summary>
public class MfaChallengeResponse
{
    public bool MfaRequired { get; set; } = true;
    /// <summary>true: the user has no authenticator yet and must set one up before getting tokens.</summary>
    public bool MfaSetupRequired { get; set; }
    public string MfaToken { get; set; } = string.Empty;
}

public class MfaTokenRequest
{
    [Required]
    public string MfaToken { get; set; } = string.Empty;
}

public class MfaEnableRequest : MfaTokenRequest
{
    [Required]
    public string Code { get; set; } = string.Empty;
}

public class MfaVerifyRequest : MfaTokenRequest
{
    public string? Code { get; set; }
    public string? RecoveryCode { get; set; }
}

public class MfaCodeRequest
{
    [Required]
    public string Code { get; set; } = string.Empty;
}

public class MfaDisableRequest
{
    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;
}

public class MfaRecoveryCodesResponse
{
    public List<string> RecoveryCodes { get; set; } = [];
}

public class MfaSetupResponse
{
    public string SharedKey { get; set; } = string.Empty;
    public string OtpAuthUri { get; set; } = string.Empty;
}

public class MfaLoginResponse : LoginResponse
{
    /// <summary>Shown once, right after MFA is enabled. Only the user sees them.</summary>
    public List<string> RecoveryCodes { get; set; } = [];
}

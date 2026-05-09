using System;

namespace NoteApp.Core
{
    public enum UserRole { Admin, Operator, User }

    public class User
    {
        public int       Id             { get; set; }
        public string    Username       { get; set; } = string.Empty;
        public string    PasswordHash   { get; set; } = string.Empty;
        public UserRole  Role           { get; set; }
        public bool      IsBlocked      { get; set; }
        public DateTime? BlockedUntil   { get; set; }
        public int       FailedAttempts { get; set; }
        public DateTime? LockoutEnd     { get; set; }
    }

    public class Note
    {
        public int      Id        { get; set; }
        public int      UserId    { get; set; }
        public string   Username  { get; set; } = string.Empty;
        public string   Content   { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class UpdateInfo
    {
        public string Version            { get; set; } = string.Empty;
        public string DownloadUrl        { get; set; } = string.Empty;
        public string Sha256             { get; set; } = string.Empty;
        public string ReleaseNotes       { get; set; } = string.Empty;
        public string MinRequiredVersion { get; set; } = "0.0.0";
    }

    public class WatchdogConfig
    {
        public int CpuThreshold { get; set; } = 85;
        public int RamThreshold { get; set; } = 90;
        public int HddThreshold { get; set; } = 80;
        public int IntervalSec  { get; set; } = 60;
    }

    public class OperationResult
    {
        public bool   Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public static OperationResult Ok(string msg = "")   => new() { Success = true,  Message = msg };
        public static OperationResult Fail(string msg = "") => new() { Success = false, Message = msg };
    }

    public class AuthResult : OperationResult
    {
        public User?  User  { get; set; }
        public string Token { get; set; } = string.Empty;
    }
}

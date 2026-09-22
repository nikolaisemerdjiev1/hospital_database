param(
    [ValidateSet('hospital_runtime', 'hospital_maintenance')]
    [string] $Role = 'hospital_runtime',
    [ValidateSet('Npgsql', 'PostgreSqlUrl')]
    [string] $Format = 'Npgsql',
    [switch] $Copy
)

function New-HospitalConnectionString {
    param(
        [Parameter(Mandatory)][string] $HostName,
        [Parameter(Mandatory)][ValidateSet('hospital_runtime', 'hospital_maintenance')][string] $Role,
        [Parameter(Mandatory)][Security.SecureString] $Password,
        [ValidateSet('Npgsql', 'PostgreSqlUrl')][string] $Format = 'Npgsql'
    )
    if ($HostName -cnotmatch '^ep-[a-z0-9-]+\.(?:[a-z0-9-]+\.)+neon\.tech$') {
        throw 'Enter only the Neon endpoint hostname, without a URL, port or credentials.'
    }
    $pooled = $HostName.Split('.')[0].EndsWith('-pooler')
    if (($Role -eq 'hospital_runtime') -ne $pooled) {
        throw 'Runtime requires the pooled host; maintenance requires the direct host.'
    }
    if ($Password.Length -eq 0) { throw 'A password is required.' }
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
    try {
        if ($Format -eq 'PostgreSqlUrl') {
            $encodedPassword = [Uri]::EscapeDataString([Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer))
            return ('postgresql://{0}:{1}@{2}/hospital_coordination?sslmode=verify-full&channel_binding=require' -f $Role, $encodedPassword, $HostName)
        }
        $builder['Host'] = $HostName
        $builder['Database'] = 'hospital_coordination'
        $builder['Username'] = $Role
        $builder['Password'] = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        $builder['SSL Mode'] = 'VerifyFull'
        $builder['Channel Binding'] = 'Require'
        $builder['Timeout'] = 15
        $builder['Command Timeout'] = 30
        $builder['Pooling'] = ($Role -eq 'hospital_runtime')
        $builder['Maximum Pool Size'] = 20
        $builder['Minimum Pool Size'] = 0
        $builder['Include Error Detail'] = $false
        return $builder.get_ConnectionString()
    }
    finally {
        $encodedPassword = $null
        $builder.Clear()
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

if ($Copy) {
    if ([Console]::IsInputRedirected) { throw 'Run interactively in your own PowerShell terminal.' }
    $hostName = Read-Host 'Neon hostname only (runtime pooled / maintenance direct)'
    $password = Read-Host 'Role password (hidden)' -AsSecureString
    $copied = $false
    try {
        $connection = New-HospitalConnectionString -HostName $hostName -Role $Role -Password $password -Format $Format
        Set-Clipboard -Value $connection
        $copied = $true
        $connection = $null
        [void](Read-Host 'Copied privately. Paste into your private vault, ignored local file or authorized secret form; press Enter to clear clipboard')
    }
    finally {
        $connection = $null
        try {
            # Windows PowerShell 5.1 can reject an empty clipboard string.
            # Replace the copied credential with harmless whitespace instead.
            if ($copied) { Set-Clipboard -Value ' ' -ErrorAction Stop }
        }
        finally { $password.Dispose() }
    }
}

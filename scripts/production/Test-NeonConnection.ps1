$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Copy-NeonConnection.ps1')
# Synthetic input only; no clipboard, file containing credentials, or network access.
$fake = ConvertTo-SecureString 'fixture;Password=injected="quoted" space' -AsPlainText -Force
try {
    foreach ($role in @('hospital_runtime', 'hospital_maintenance')) {
        $hostName = if ($role -eq 'hospital_runtime') { 'ep-fixture-pooler.us-west-2.aws.neon.tech' } else { 'ep-fixture.us-west-2.aws.neon.tech' }
        $text = New-HospitalConnectionString -HostName $hostName -Role $role -Password $fake
        $parsed = New-Object System.Data.Common.DbConnectionStringBuilder
        $parsed.set_ConnectionString($text)
        if ($parsed['Password'] -cne 'fixture;Password=injected="quoted" space' -or
            $parsed['Username'] -cne $role -or $parsed['Database'] -cne 'hospital_coordination' -or
            $parsed['SSL Mode'] -cne 'VerifyFull' -or $parsed['Channel Binding'] -cne 'Require') {
            throw 'Connection-string quoting or security flags failed.'
        }
        $url = [Uri](New-HospitalConnectionString -HostName $hostName -Role $role -Password $fake -Format PostgreSqlUrl)
        $parts = $url.UserInfo.Split(':', 2)
        if ($url.Scheme -cne 'postgresql' -or $url.Host -cne $hostName -or $parts[0] -cne $role -or
            [Uri]::UnescapeDataString($parts[1]) -cne 'fixture;Password=injected="quoted" space' -or
            $url.AbsolutePath -cne '/hospital_coordination' -or
            $url.Query -cne '?sslmode=verify-full&channel_binding=require') {
            throw 'URL encoding or security flags failed.'
        }
    }
    foreach ($badHost in @('https://ep-fixture-pooler.us-west-2.aws.neon.tech',
        'ep-fixture-pooler.us-west-2.aws.neon.tech;Password=bad',
        'ep-fixture-pooler.evil.example', 'ep-fixture.us-west-2.aws.neon.tech')) {
        $rejected = $false
        try { [void](New-HospitalConnectionString -HostName $badHost -Role hospital_runtime -Password $fake) }
        catch { $rejected = $true }
        if (-not $rejected) { throw 'Unsafe or incorrect endpoint was accepted.' }
    }
    $rejected = $false
    try { [void](New-HospitalConnectionString -HostName 'ep-fixture-pooler.us-west-2.aws.neon.tech' -Role hospital_maintenance -Password $fake) }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Pooled maintenance endpoint was accepted.' }
    Write-Output 'PASS: 4 Npgsql/URL role/quoting/security cases and 5 endpoint rejection cases; no clipboard or network used.'
}
finally { $fake.Dispose() }

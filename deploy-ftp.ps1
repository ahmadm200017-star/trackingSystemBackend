# Uploads publish-selfcontained/ to the site4now FTP account.
#
#   .\deploy-ftp.ps1 -Password 'yourpassword' -List          # inspect remote layout first
#   .\deploy-ftp.ps1 -Password 'yourpassword'                # deploy to /
#   .\deploy-ftp.ps1 -Password 'yourpassword' -RemoteRoot '/site1/wwwroot'
#
# Takes app_offline.htm up first so IIS releases its lock on MdfTracker.Api.exe,
# then removes it at the end to bring the app back.

param(
    [Parameter(Mandatory = $true)][string]$Password,
    [string]$Server = 'win8113.site4now.net',
    [string]$User = 'ahmad12122-001',
    [string]$RemoteRoot = '/',
    [string]$LocalDir = 'publish-selfcontained',
    # Files matching this are never size-matched, always re-sent. Everything else - the
    # ~350 third-party runtime assemblies, which only change when a package version does -
    # keeps the size-based skip, so a deploy stays fast.
    [string]$AlwaysUpload = '(\.(json|config|xml|html|htm|txt|css|js|pdb)$)|(^MdfTracker\.Api)',
    [switch]$List
)

$ErrorActionPreference = 'Stop'
$cred = New-Object System.Net.NetworkCredential($User, $Password)

function New-FtpRequest($uri, $method) {
    $req = [System.Net.FtpWebRequest]::Create($uri)
    $req.Credentials = $cred
    $req.Method = $method
    $req.UsePassive = $true
    $req.UseBinary = $true
    $req.KeepAlive = $false
    $req.Timeout = 60000
    return $req
}

function Get-FtpUri($remotePath) {
    $clean = ($remotePath -replace '\\', '/') -replace '/+', '/'
    return "ftp://$Server" + $clean
}

function Get-FtpListing($remotePath) {
    $req = New-FtpRequest (Get-FtpUri $remotePath) ([System.Net.WebRequestMethods+Ftp]::ListDirectoryDetails)
    $reader = New-Object System.IO.StreamReader $req.GetResponse().GetResponseStream()
    $out = $reader.ReadToEnd()
    $reader.Close()
    return $out
}

function New-FtpDirectory($remotePath) {
    try {
        $req = New-FtpRequest (Get-FtpUri $remotePath) ([System.Net.WebRequestMethods+Ftp]::MakeDirectory)
        $req.GetResponse().Close()
    } catch [System.Net.WebException] {
        # 550 = already exists. Anything else is real.
        if ($_.Exception.Response.StatusCode -ne [System.Net.FtpStatusCode]::ActionNotTakenFileUnavailable) { throw }
    }
}

function Send-FtpFile($localPath, $remotePath) {
    # Shared hosts run out of passive data ports under rapid-fire connections
    # and answer 425. Back off and retry rather than losing the whole run.
    $attempt = 0
    while ($true) {
        $attempt++
        try {
            $req = New-FtpRequest (Get-FtpUri $remotePath) ([System.Net.WebRequestMethods+Ftp]::UploadFile)
            $bytes = [System.IO.File]::ReadAllBytes($localPath)
            $req.ContentLength = $bytes.Length
            $stream = $req.GetRequestStream()
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Close()
            $req.GetResponse().Close()
            return
        } catch {
            if ($attempt -ge 5) { throw }
            Start-Sleep -Seconds ([Math]::Min(15, [Math]::Pow(2, $attempt)))
        }
    }
}

# name -> size for one remote directory, so an interrupted run can resume.
function Get-RemoteFileMap($remotePath) {
    $map = @{}
    try { $listing = Get-FtpListing $remotePath } catch { return $map }
    foreach ($line in ($listing -split "`n")) {
        if ($line -match '^\d\d-\d\d-\d\d\s+\d\d:\d\d[AP]M\s+(<DIR>|\d+)\s+(.+?)\s*$') {
            if ($Matches[1] -ne '<DIR>') { $map[$Matches[2]] = [long]$Matches[1] }
        }
    }
    return $map
}

function Remove-FtpFile($remotePath) {
    try {
        $req = New-FtpRequest (Get-FtpUri $remotePath) ([System.Net.WebRequestMethods+Ftp]::DeleteFile)
        $req.GetResponse().Close()
    } catch [System.Net.WebException] { }
}

if ($List) {
    Write-Host "Listing $RemoteRoot on $Server`n" -ForegroundColor Cyan
    Get-FtpListing $RemoteRoot
    exit 0
}

$root = (Resolve-Path $LocalDir).Path
$files = Get-ChildItem $root -Recurse -File
Write-Host "Deploying $($files.Count) files to ftp://$Server$RemoteRoot" -ForegroundColor Cyan

# 1. Stop the app so its .exe and .dll files are unlocked.
$offline = Join-Path $env:TEMP 'app_offline.htm'
Set-Content $offline '<html><body>Deploying, back in a minute.</body></html>' -Encoding UTF8
Send-FtpFile $offline "$RemoteRoot/app_offline.htm"
Write-Host 'app_offline.htm uploaded - app stopped.' -ForegroundColor Yellow
Start-Sleep -Seconds 3

# 2. Push everything, creating directories as they are first needed.
#    Files already on the server at the exact same size are skipped, so a run
#    that died partway can simply be started again.
$existing = Get-RemoteFileMap $RemoteRoot
Write-Host "$($existing.Count) files already on server - matching ones will be skipped." -ForegroundColor DarkGray

$made = @{}
$i = 0; $sent = 0; $skipped = 0
foreach ($f in $files) {
    $i++
    $rel = $f.FullName.Substring($root.Length).TrimStart('\')
    $remote = "$RemoteRoot/" + ($rel -replace '\\', '/')

    $dir = Split-Path $rel -Parent
    if ($dir -and -not $made.ContainsKey($dir)) {
        $parts = $dir -split '\\'
        $acc = $RemoteRoot
        foreach ($p in $parts) {
            $acc = "$acc/$p"
            New-FtpDirectory $acc
        }
        $made[$dir] = $true
    }

    # Resume support: skip a file already on the server at the same size.
    #
    # Size is a weak identity check, so it is not trusted for anything that can change
    # content without changing length. Two cases actually bite:
    #   - a Vite index.html is a constant size across builds, because the asset hash it
    #     points at is always the same length, so a changed one looked "already present"
    #     and the deploy silently shipped HTML referencing the previous build's bundle;
    #   - a recompiled assembly can land on the same byte count, which would strand the
    #     old code on the server with no sign anything went wrong.
    # Both are covered by $AlwaysUpload. Third-party runtime files are left to the skip:
    # they only change when a package version changes, which changes their size too.
    if (-not $dir -and
        $f.Name -notmatch $AlwaysUpload -and
        $existing.ContainsKey($f.Name) -and
        $existing[$f.Name] -eq $f.Length) {
        $skipped++
        continue
    }

    Write-Progress -Activity 'Uploading' -Status "$rel  (sent $sent, skipped $skipped)" -PercentComplete (100 * $i / $files.Count)
    Send-FtpFile $f.FullName $remote
    $sent++
}
Write-Progress -Activity 'Uploading' -Completed
Write-Host "Uploaded $sent, skipped $skipped (already present)." -ForegroundColor Cyan

# 3. Bring it back.
Remove-FtpFile "$RemoteRoot/app_offline.htm"
Write-Host "`nDone. app_offline.htm removed - app restarting." -ForegroundColor Green
Write-Host 'Check https://ahmad12122-001-site1.gtempurl.com/  (if it still fails, read logs/stdout_*.log over FTP)'

$scriptPath = "d:\workspace\internetspeedtracker\NetParity.ps1"
$content = Get-Content $scriptPath -Raw
$content = $content -replace "\[void\]\$window\.ShowDialog\(\)", "`$window.Show(); Start-Sleep -s 3; `$window.Close()"
Invoke-Expression $content

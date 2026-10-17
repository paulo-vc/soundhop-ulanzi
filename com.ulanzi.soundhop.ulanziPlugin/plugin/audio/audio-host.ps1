# Long-running helper used by the Node main service.
# Protocol (one line per message, UTF-8):
#   in : <reqId> list
#   in : <reqId> set <deviceId>
#   out: {"id":<reqId>,"ok":true,"data":...} | {"id":<reqId>,"ok":false,"error":"..."}
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8

function Write-Line([string]$s) {
    [Console]::Out.WriteLine($s)
    [Console]::Out.Flush()
}

function Esc([string]$s) {
    return ($s -replace '\\', '\\' -replace '"', '\"' -replace "`r", ' ' -replace "`n", ' ')
}

try {
    $source = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'AudioEndpoints.cs'))
    Add-Type -TypeDefinition $source -Language CSharp
} catch {
    Write-Line ('{"id":0,"ok":false,"error":"' + (Esc $_.Exception.Message) + '"}')
    exit 1
}

Write-Line '{"id":0,"ok":true,"data":"ready"}'

while ($true) {
    $line = [Console]::In.ReadLine()
    if ($null -eq $line) { break }
    $line = $line.Trim()
    if ($line -eq '') { continue }

    $parts = $line.Split(' ', 3)
    $id = $parts[0]
    $cmd = if ($parts.Length -gt 1) { $parts[1] } else { '' }
    try {
        switch ($cmd) {
            'list' {
                Write-Line ('{"id":' + $id + ',"ok":true,"data":' + [UlanziAudio.Endpoints]::List() + '}')
            }
            'set' {
                [UlanziAudio.Endpoints]::SetDefault($parts[2])
                Write-Line ('{"id":' + $id + ',"ok":true,"data":null}')
            }
            'exit' { exit 0 }
            default {
                Write-Line ('{"id":' + $id + ',"ok":false,"error":"unknown command"}')
            }
        }
    } catch {
        Write-Line ('{"id":' + $id + ',"ok":false,"error":"' + (Esc $_.Exception.Message) + '"}')
    }
}

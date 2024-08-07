Import-Module ./devUtils/setWindow.psm1

function Get-EnvFromFile([string]$FilePath) {
    Write-Host $FilePath
    if (Test-Path $FilePath) {
        Write-Host (Get-Content $FilePath)
        Get-Content $FilePath | ForEach-Object {
            Write-Host "looop"
            $name, $value = $_.split('=')
            Write-Host $name $value
            if ([string]::IsNullOrWhiteSpace($name) || $name.Contains('#')) {
                continue
            }
            Set-Content env:\$name $value
        }
    } else {
        Write-Warning "Environment file not found: $FilePath"
    }
}


Get-EnvFromFile (Join-Path $PSScriptRoot ".env")
Get-EnvFromFile (Join-Path $PSScriptRoot ".env.local")


Get-Process "SUMMERHOUSE" | Stop-Process

# Check if required environment variables are set
if (-not $env:SUMMERHOUSE_EXECUTABLE_DIR_PATH) {
    throw "SUMMERHOUSE_EXECUTABLE_DIR_PATH is not set. Please check your env or env.local file."
}

New-Item -Path (Join-Path $env:SUMMERHOUSE_EXECUTABLE_DIR_PATH '\BepInEx\plugins\') -ItemType "directory" -Name SummerhouseFlipped
Remove-Item -Force (Join-Path $env:SUMMERHOUSE_EXECUTABLE_DIR_PATH '\BepInEx\plugins\SummerhouseFlipped\*')

Copy-Item -Recurse -Force '.\bin\Debug\netstandard2.1\*' (Join-Path $env:SUMMERHOUSE_EXECUTABLE_DIR_PATH 'BepInEx\plugins\SummerhouseFlipped\')


# Define the path to your game executable
$gamePath = (Join-Path $env:SUMMERHOUSE_EXECUTABLE_DIR_PATH '\SUMMERHOUSE.exe')



# Launch the game asynchronously
Start-Process -FilePath $gamePath -NoNewWindow -PassThru

# Hacky way to separate the console window from the game window so they're
# both visible
Sleep -Seconds 1

Set-Window -Id (Get-Process Summerhouse ).Id -PassThru -X -1403 -Y -835 -Width 1240 -Height 686

Sleep -Seconds 5

Set-Window -Id (Get-Process Summerhouse ).Id -PassThru -X 10 -Y 10 -Width 1240 -Height 686


$host.ui.RawUI.WindowTitle = "Summerhouse Launch Manager"



# Kill self even in Windows Terminals where profile is set to keep exited processes open

Get-Process | Where-Object {$_.MainWindowTitle -like "Summerhouse Launch Manager"} | Stop-Process

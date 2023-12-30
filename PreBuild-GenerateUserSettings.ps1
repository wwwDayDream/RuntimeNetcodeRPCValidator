# constant declarations
$userFileExt = ".user"
$templateFileName = "assets\CSPROJ.USER.TEMPLATE.xml"

function FormTemplate($templateFile, $gameDirectory, $bepInDirectory)
{
    $template = (Get-Content ($templateFile + $templateFileName))
    $template = $template.Replace("@(GAMEDIR)", $gameDirectory).Replace("@(BEPINDIR)", $bepInDirectory)
    return $template
}

# main logic
Write-Host "File " ($args[0] + $userFileExt) " doesn't exist. Creating now.."
$gameDir = Read-Host -Prompt "Enter Game directory"
$bepInDir = Read-Host -Prompt "Enter games BepInEx directory"
$templateFilePath = args[1]

$template = FormTemplate $templateFilePath $gameDir $bepInDir
Set-Content -Path ($args[0] + $userFileExt) -Value $template
Exit 1
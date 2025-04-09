@REM This comment prevents possible BOM problems
@echo off

if "%1"=="" (
    echo No version specified as argument
    exit /b
)
@echo Releasing Version %1

echo Releasing [win-x64]
call dotnet publish -r win-x64 -c Release --self-contained /p:PublishSingleFile=true /p:PublishTrimmed=true

if %errorlevel% neq 0 (
    echo Failed to release [win-x64]
    exit /b %errorlevel%
)

echo Releasing [linux-x64]
dotnet publish -r linux-x64 -c Release --self-contained /p:PublishSingleFile=true /p:PublishTrimmed=true

if %errorlevel% neq 0 (
    echo Failed to release [linux-x64]
    exit /b %errorlevel%
)

echo Releasing [linux-arm64]
dotnet publish -r linux-arm64 -c Release --self-contained /p:PublishSingleFile=true /p:PublishTrimmed=true

if %errorlevel% neq 0 (
    echo Failed to release [linux-arm64]
    exit /b %errorlevel%
)

SET DOTNET_VERSION=net7.0
SET PROJECT_NAME=subtitle-downloader
SET VERSION_DIR=bin\Release\%1

rmdir /s /q %VERSION_DIR%
mkdir %VERSION_DIR%
echo Moving files to version directory at %VERSION_DIR% and renaming

move "bin\Release\%DOTNET_VERSION%\win-x64\publish\%PROJECT_NAME%.exe" %VERSION_DIR%
rename "%VERSION_DIR%\%PROJECT_NAME%.exe" subs-win-x64.exe 

move "bin\Release\%DOTNET_VERSION%\linux-x64\publish\%PROJECT_NAME%" %VERSION_DIR%
rename "%VERSION_DIR%\%PROJECT_NAME%" subs-linux-x64

move "bin\Release\%DOTNET_VERSION%\linux-arm64\publish\%PROJECT_NAME%" %VERSION_DIR%
rename "%VERSION_DIR%\%PROJECT_NAME%" subs-linux-arm64


@echo on

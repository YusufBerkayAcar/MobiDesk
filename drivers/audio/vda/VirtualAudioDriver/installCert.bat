@echo off

set CERTIFICATE="%~dp0MobiDeskAudio.cer"

certutil -addstore -f root %CERTIFICATE%
certutil -addstore -f TrustedPublisher %CERTIFICATE%
pause

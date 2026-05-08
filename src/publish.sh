#!/bin/bash

ssh rhea@titan "sudo systemctl stop gdm"

# trash GnomeSurfaceSession/src/obj
# trash GnomeSurfaceSession/src/bin
# trash GnomeSurfaceShell/src/obj
# trash GnomeSurfaceShell/src/bin

dotnet publish GnomeSurfaceSession/src/GnomeSurfaceSession.csproj -p:PublishProfile=Release.pubxml -r linux-x64
dotnet publish GnomeSurfaceShell/src/GnomeSurfaceShell.csproj -p:PublishProfile=Release.pubxml -r linux-x64

ssh rhea@titan "sudo cp -f /home/rhea/core/ /opt/gnome-surface/core/"
ssh rhea@titan "sudo systemctl start gdm"

#!/bin/bash

IP=$(hostname -I | awk '{print $2}')

echo "## START Orbital-Shell install "$IP

echo " -> Update repository"
apt-get update -qq >/dev/null

echo " -> Install .Net (SDK, Runtime, AspNet Core // v3.1 + v5.0)"
wget https://packages.microsoft.com/config/debian/10/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

apt-get install -qq -y apt-transport-https git dotnet-sdk-3.1 dotnet-sdk-5.0 dotnet-runtime-3.1 dotnet-runtime-5.0 \
                        aspnetcore-runtime-3.1 aspnetcore-runtime-5.0 >/dev/null

echo " -> Download Orbital-Shell"
mkdir -p /home/orbsh
git clone https://github.com/OrbitalShell/Orbital-Shell.git	

dotnet build ./Orbital-Shell/OrbitalShell.sln
sudo ln -s ./Orbital-Shell/OrbitalShell-CLI/bin/Debug/netcoreapp3.1/orbsh /bin/orbsh

echo "## END"
#/bin/bash

echo "#### Vagrant VM builder v0.1"
VMPATH=$(dirname $(readlink -f $0))
cd $VMPATH

if [ -d .vagrant ];
then
    read -p 'Would you like to remove the current VM (Y/N): ' rep

    if [ $(echo $rep|sed 's/.*/\U&/') == 'Y' ];
    then
        vagrant halt $HOST
        vagrant destroy $HOST
        rm -R .vagrant
        rm Vagrantfile;
    else
        exit;
    fi;
fi;


echo "# Load VM definition"
. ./conf/vmdef.sh


echo "# Build Vagrantfile"
cp ./conf/Vagrantfile.tpl Vagrantfile

sed -i "s/#HOST#/$HOST/g" Vagrantfile
sed -i "s/#VMDEF#/$VMDEF/g" Vagrantfile
sed -i "s/#VMIP#/$VMIP/g" Vagrantfile
sed -i "s/#VMMEM#/$VMMEM/g" Vagrantfile
sed -i "s/#VMCPU#/$VMCPU/g" Vagrantfile


echo "# Deploy VM"
vagrant plugin install vagrant-vbguest
vagrant up $HOST


echo "# Add the VM IP into /etc/hosts file"
if test -z "$(grep "# Vagrant's VMs list" /etc/hosts)"; 
then
    sudo echo -e "\n# Vagrant's VMs list" >> /etc/hosts;
fi;

# Remove the old VM entry
if ! test -z "$(grep $VM /etc/hosts)"; 
then
   sudo sed -i"*.bak" "/$(grep $VM /etc/hosts)/"d /etc/hosts
fi;

# Add the new VM entry
sudo sed -i "/# Vagrant's VMs list/a$VMIP\t$VM" /etc/hosts


echo "# Add VM into SSH config"
SSHCONF="Host $VM\n\tHostname $VMIP\n\tPort 22\n\tIdentityFile $VMPATH/.vagrant/machines/$HOST/virtualbox/private_key\n\tUser vagrant\n"

if ! test -z "$(grep "Host $VM" ~/.ssh/config)"; 
then
    # Remove the old VM entry
    ROWNB=$(grep -n "Host $VM" ~/.ssh/config)
    sed -i"*.bak" "${ROWNB/":Host "$VM/""},6d" ~/.ssh/config;
fi;

# Add the new VM entry
sed -i "1i$SSHCONF" ~/.ssh/config;

echo "Done."
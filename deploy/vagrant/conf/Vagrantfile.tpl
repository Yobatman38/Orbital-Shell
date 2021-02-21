# -*- mode: ruby -*-
# vi: set ft=ruby :

Vagrant.configure("2") do |config|
  config.vm.define "#HOST#" do |vmdef|
    vmdef.vm.box = "debian/buster64"
    vmdef.vm.hostname = "#HOST#"
    vmdef.vm.box_url = "debian/buster64"
    # vmdef.vm.network "public_network"
    vmdef.vm.network "private_network", ip: "#VMIP#"
    vmdef.vm.provider "virtualbox" do |v|
      # v.customize ["modifyvm", :id, "--natdnshostresolver1", "on"]
      # v.customize ["modifyvm", :id, "--natdnsproxy1", "on"]
      v.customize ["modifyvm", :id, "--memory", #VMMEM#]
      v.customize ["modifyvm", :id, "--name", "#VMDEF#"]
      v.customize ["modifyvm", :id, "--cpus", "#VMCPU#"]
    end
    config.vm.provision "shell", inline: <<-SHELL
      sed -i 's/ChallengeResponseAuthentication no/ChallengeResponseAuthentication yes/g' /etc/ssh/sshd_config    
      service ssh restart
    SHELL
    vmdef.vm.provision "shell", path: "./conf/vm_provision.sh"
  end
end
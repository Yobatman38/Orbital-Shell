---
layout: documents
title: Repository And Projects
doctitle: Repository And Projects
---
<h3>Contents of the Orbital Shell project repository</h3>
Using **PowerShell**,**git** and **dotnet**, you get the Orbital Shell repository:
<pre data-enlighter-language="shell" data-enlighter-theme="{{site.data.settings.enjs_shell_theme}}" data-enlighter-linenumbers="false">
# PowerShell
cd ~
git clone https://github.com/franck-gaspoz/orbital-shell.git
cd ./orbital-shell
dir
</pre>
you get the following folders ands files:
<pre data-enlighter-language="shell" data-enlighter-theme="rowhammer" data-enlighter-linenumbers="false">
Mode                LastWriteTime         Length Name
----                -------------         ------ ----
d-----       04/01/2021     16:02                .vscode
d-----       25/09/2020     19:33                Doc
d-----       05/01/2021     22:13                docs
d-----       08/01/2021     04:48                DotNetConsoleAppToolkit
d-----       25/09/2020     21:18                DotNetConsoleAppToolkit-Shell
d-----       26/09/2020     00:49                OrbitalShell
-a----       20/05/2020     13:05           2581 .gitattributes
-a----       20/05/2020     13:05           6084 .gitignore
-a----       25/09/2020     18:23           3815 dotnet-console-app-toolkit.sln
-a----       13/06/2020     04:12           2542 LICENSE.md
-a----       25/09/2020     21:54           5738 README.md
</pre>

Let's see what are these files and folders in detail :

|------+------+-------------| 
| name | type | description |
|------+------+-------------| 
|**Doc**|markdown static web site|GitHub .md documentation files|
|**docs**|Jekyll web site|Orbital Shell project Git Pages|
|=================+============+=================+================|
|**DotNetConsoleAppToolkit**|DotNet standard lib 2.1|.net console application toolkit|
|**DotNetConsoleAppToolkit-Shell**|DotNet standard lib 2.1|.net console application toolkit : shell extension |
|**OrbitalShell**|DotNet Core 5 console app|Orbital Shell CLI|
{:.rsp-table .light-table}

So we have 3 .NetCore projects folders and 2 documentation folders.
We also have two **solution** projects files:
* **.vscode** is used by **vscode** when opening the repository as a solution (<code>vscode .</code>)
* **.sln** is used to open project files in a **Visual Studio** (2019) solution.

Let's watch to these folders in detail:
{:.marginbottom}

<h4 class="doc-subtitle">Doc</h4>

Any files related to GitHub markdown files that can be browsed at GitHub web repository location: [https://github.com/franck-gaspoz/orbital-shell](https://github.com/franck-gaspoz/orbital-shell)
These files are linked by the files at repository root:
* README.md

<h4 class="doc-subtitle">docs</h4>

These is the **gh-pages** folder, containing the files for the **GitHub** pages of Orbital Shell [GitHub Project](https://github.com/users/franck-gaspoz/projects/1)
This is the content of a **Jekyll** static web site generator based on **Liquid**, **kramdown**, **Ruby**
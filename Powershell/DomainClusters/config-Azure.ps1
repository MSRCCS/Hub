##############################################################################
## User specific configuration
## Author: Jin Li
## Date: Apr. 2015                                                      
## Configuration specific to each user (please edit this file for your own usage)
##############################################################################
if (-Not $UserAlias) {
	# userAlias string, no blank space or special characters are allowed
	Set-Variable -Name UserAlias -Value 'imhub' -Scope 1
}


if (-Not $cluster) {
	# name of the cluster used in execution
	Set-Variable -Name cluster -Value '\\yuxiao-z840\Prajna\Cluster\Prajna_' -Scope 1
}

if (-Not $port) {
	# port used by your client daemon, default is 1082 for Azure VMs
	Set-Variable -Name port -Value 1082 -Scope 1
}

if (-Not $jobport) {
	# port used by your client services
	Set-Variable -Name jobport -Value '1400-1449' -Scope 1
}
	
if (-Not $rootSrcFolder) {
	# A shared folder to deploy the application. The preferred share folder is located on a server, as Windows client OS has limitation on # of shared file access that it can host
	Set-Variable -Name rootSrcFolder -Value '\\yuxiao-z840\share\PrajnaSource' -Scope 1
}

if (-Not $localSrcFolder) {
	# Src folder that contains the latest PrajnaClient code in the local machine
	Set-Variable -Name localSrcFolder -Value '\\yuxiao-z840\Src\SkyNet' -Scope 1
}

if (-Not $targetSrcDir) {
	# Src folder that contains the latest PrajnaClient code in the remote machine
	Set-Variable -Name targetSrcDir -Value "\PrajnaSource$UserAlias" -Scope 1
}

if (-Not $logdir) {
	# Log folder at remote machine
	Set-Variable -Name logdir -Value "c:\OneNet\Log\$UserAlias" -Scope 1
}

if (-Not $verbose) {
	# verbose level of the test program that executes at the local machine
	# 3: minimum log
	# 4: if you are intersted to see operation details. 
	# 6: detailed log (may severely impact performance)
	Set-Variable -Name verbose -Value '4' -Scope 1
}

if (-Not $clientverbose) {
	# verbose level of the remote program that executes in the cluster
	# 3: minimum log
	# 4: if you are intersted to see operation details. 
	# 6: detailed log (may severely impact performance)
	Set-Variable -Name clientverbose -Value '4' -Scope 1
}

if (-Not $homein) {
	# report back server
	# This is the machine in which the PrajnaController will run to capture the cluster configuration information. 
	# It only needs to be run once. 
	Set-Variable -Name homein -Value 'imhub-CUS.cloudapp.net' -Scope 1
} 

if (-Not $memsize) {
	# memory limitation of the Prajna program. 
	Set-Variable -Name memsize -Value 48000 -Scope 1
} 


if (-Not $CredentialFile) {
	# Holds the remote credential used to launch the Prajna daemon program (PrajnaClient.exe)
	Set-Variable -Name CredentialFile -Value "C:\Users\$UserAlias\Documents\CredFile-Azure" -Scope 1
}

if (-Not $User) {
	# Holds the remote username used to launch the Prajna daemon program (PrajnaClient.exe)
	Set-Variable -Name User -Value "localhost\$UserAlias" -Scope 1
}

if (-Not $LocalExeFolder ) 
{
	# Local folder to hold compiled Prajna executables. 
	Set-Variable -Name LocalExeFolder -Value "c:\OneNet" -Scope 1
}

# Additional Deployment folder to be copied to the remote machine. 

Set-Variable -Name AdditionalDeploy -Value @"
robocopy $localSrcFolder'\PrajnaSamples' $rootSrcFolder'\PrajnaSamples' /s /mir 
"@ -Scope 1

# For unit test
if (-Not $remoteDKVName) {
	# DKV name used in read/write test
	Set-Variable -Name remoteDKVName -Value jinl\1012_pick -Scope 1
}

if (-Not $localDirName) {
	# folder of files that you write to the cluster
	# a common test is to write a folder worth of image to the remote cluster and read them back. 
	Set-Variable -Name localDirName -Value "h:\album\pick" -Scope 1
}

if (-Not $localSaveDir) {
	# folder where the set of files get to read back. It should hold the same set of files as those in localDirName after a write/read cycle completes. 
	Set-Variable -Name localSaveDir -Value "e:\data\1012_pick" -Scope 1
}

if (-Not $localFVDir) {
	# compute the fisher vector of the folder of images, and stored the result in the folder below. 
	Set-Variable -Name localFVDir -Value "e:\data\fishervector" -Scope 1
}

if (-Not $FVDependenciesDir) {
	# the DLLs used by the fisher vector program. 
	Set-Variable -Name FVDependenciesDir -Value "H:\msr\2015\work\fsharp\PrajnaSamples\FisherVector\dependencies" -Scope 1
}


if (-Not $uploadfile ) {
	# (uploadfile) contains a list of URLs to be distributed crawled by DistributedWebCrawler.exe
	# each line of the file contains tab separated information. The (uploadkey)th column contaisn the URL to be crawled. 
	Set-Variable -Name uploadfile -Value "\\research\root\share\jinl\image.list.txt" -Scope 1
}

if (-Not $uploadkey ) {
	# (uploadfile) contains a list of URLs to be distributed crawled by DistributedWebCrawler.exe
	# each line of the file contains tab separated information. The (uploadkey)th column contaisn the URL to be crawled. 
	Set-Variable -Name uploadkey -Value "4" -Scope 1
}

if (-Not $uploadRemote) {
	# After distributed crawling, the images are stored in DKV named (uploadRemote)
	Set-Variable -Name uploadRemote -Value "ImBase\image.list" -Scope 1
}

if (-Not $remoteVector) {
	# The DKV name used in Kmeans test
	Set-Variable -Name remoteVector -Value "jinl\data\VectorGen" -Scope 1 
}


if (-Not $ParallelDownload ) 
{
	# parallel download tasks to launch for DistributedWebCrawler.exe
	Set-Variable -Name ParallelDownload -Value "200" -Scope 1
}

if (-Not $DownloadDir ) 
{
	# When reading back the downloaded file, the folder to store the crawled download directory
	Set-Variable -Name DownloadDir -Value "e:\data\download" -Scope 1
}

# end: for unit test



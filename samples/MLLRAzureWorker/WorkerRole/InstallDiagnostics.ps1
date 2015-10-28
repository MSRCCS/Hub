# Configurations
$storage_name = ""
$key = ""
$config_path=""
$service_name=""
$worker_role_name="WorkerRole"

# Install
$storageContext = New-AzureStorageContext -StorageAccountName $storage_name -StorageAccountKey $key 
Set-AzureServiceDiagnosticsExtension -StorageContext $storageContext -DiagnosticsConfigurationPath $config_path -ServiceName $service_name -Slot Production -Role $worker_role_name

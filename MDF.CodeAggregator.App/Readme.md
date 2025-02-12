# Move to project App
cd .\MDF.CodeAggregator.App\

# pack
dotnet pack

# install
dotnet tool install --global --add-source ./nupkg MDF.CodeAggregator.App

# remove
dotnet tool uninstall -g microsoft.botsay
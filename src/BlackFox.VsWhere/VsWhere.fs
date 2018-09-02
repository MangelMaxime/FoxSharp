namespace BlackFox.VsWhere

open System
open System.Runtime.InteropServices

module VsInstances =
    let private setupConfiguration = lazy(
        let configType = System.Type.GetTypeFromCLSID (System.Guid "177F0C4A-1CD3-4DE7-A32C-71DBBB9FA36D")
        Activator.CreateInstance configType :?> ISetupConfiguration
    )

    let private enumAllInstances () =
        let instancesEnumerator =
            let v1 = setupConfiguration.Value
            match v1 with
            | :? ISetupConfiguration2 as v2 -> v2.EnumAllInstances()
            | _ -> v1.EnumInstances()
        let instances = Array.zeroCreate<ISetupInstance> 1
        let fetched = ref 1
        seq {
            while !fetched = 1 do
                instancesEnumerator.Next(1, instances, fetched)
                if !fetched = 1 then
                    yield instances.[0]
        }

    let private parseErrorInfo (error: ISetupErrorInfo) =
        {
            HResult = error.GetErrorHResult()
            ErrorClassName = error.GetErrorClassName()
            ErrorMessage = error.GetErrorMessage()
        }

    let private parsePackageReference (instance: ISetupPackageReference) =
        {
            Id = instance.GetId()
            Version = instance.GetId()
            Chip = instance.GetChip()
            Language = instance.GetLanguage()
            Branch = instance.GetBranch()
            Type = instance.GetType()
            UniqueId = instance.GetUniqueId()
            IsExtension = instance.GetIsExtension()
        }

    let private parseErrorState (state: ISetupErrorState) =
        let result =
            {
                FailedPackages = state.GetFailedPackages() |> Seq.map parsePackageReference |> List.ofSeq
                SkippedPackages = state.GetSkippedPackages() |> Seq.map parsePackageReference |> List.ofSeq
                ErrorLogFilePath = None
                LogFilePath = None
                RuntimeError = None
            }

        match state with
        | :? ISetupErrorState2 as state2 ->
            let result2 =
                { result with
                    ErrorLogFilePath = state2.GetErrorLogFilePath() |> Some
                    LogFilePath = state2.GetLogFilePath() |> Some
                }
            match state2 with
            | :? ISetupErrorState3 as state3 ->
                { result2 with
                    RuntimeError = state3.GetRuntimeError() |> parseErrorInfo |> Some
                }
            | _-> result2
        | _ -> result

    let private parseDate (date: System.Runtime.InteropServices.ComTypes.FILETIME) =
        let high = uint64 (uint32 date.dwHighDateTime)
        let low = uint64 (uint32 date.dwLowDateTime)
        let composed = (high <<< 32) ||| low
        DateTimeOffset.FromFileTime(int64 composed)

    let private parseProperties (store: ISetupPropertyStore) =
        store.GetNames()
        |> Seq.map(fun name ->
            let value = store.GetValue(name)
            name, value)
        |> Map.ofSeq

    let private parseInstance (instance: ISetupInstance) =
        let mutable result =
          { InstanceId = instance.GetInstanceId()
            InstallDate = parseDate (instance.GetInstallDate())
            InstallationName = instance.GetInstallationName()
            InstallationPath = instance.GetInstallationPath()
            InstallationVersion = instance.GetInstallationVersion()
            DisplayName = instance.GetDisplayName(0)
            Description = instance.GetDescription(0)
            State = None
            Packages = []
            Product = None
            ProductPath = None
            Errors = None
            IsLaunchable = None
            IsComplete = None
            Properties = Map.empty
            EnginePath = None
            IsPrerelease = None
            CatalogInfo = Map.empty }

        match instance with
        | :? ISetupInstanceCatalog as catalog ->
            result <- { result with
                          IsPrerelease = catalog.IsPrerelease() |> Some
                          CatalogInfo = catalog.GetCatalogInfo() |> parseProperties }
        | _ -> ()

        match instance with
        | :? ISetupInstance2 as v2 ->
            { result with
                State = v2.GetState() |> Some
                Packages = v2.GetPackages() |> Seq.map parsePackageReference |> List.ofSeq
                Product = parsePackageReference (v2.GetProduct()) |> Some
                ProductPath = v2.GetProductPath() |> Some
                Errors = v2.GetErrors() |> Option.ofObj |> Option.map parseErrorState
                IsLaunchable = v2.IsLaunchable() |> Some
                IsComplete = v2.IsComplete() |> Some
                Properties = parseProperties (v2.GetProperties())
                EnginePath = v2.GetEnginePath() |> Some }
        | _ -> result

    let getAll () =
        try
            enumAllInstances ()
            |> Seq.map parseInstance
            |> List.ofSeq
        with
        | :? COMException ->
            []

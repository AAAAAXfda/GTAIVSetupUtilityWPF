using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace GTAIVSetupUtilityWPF.Functions
{
    public static class VulkanChecker
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        static (int, int) ConvertApiVersion(uint apiversion)
        {
            uint major = apiversion >> 22;
            uint minor = apiversion >> 12 & 0x3ff;
            return (Convert.ToInt32(major), Convert.ToInt32(minor));
        }

        public static (int, int, bool, bool, bool, bool) VulkanCheck()
        {
            int gpuCount = 0;
            int dgpuDxvkSupport = 0;
            int igpuDxvkSupport = 0;
            bool igpuOnly = true;
            bool dgpuOnly = true;
            bool intelIgpu = false;
            bool nvidiaGpu = false;

            while (true)
            {
                Logger.Debug($"Running vulkaninfo on GPU{gpuCount}... If this infinitely loops, your GPU is weird!");
                Process process = new Process();
                process.StartInfo.FileName = "vulkaninfo";
                process.StartInfo.Arguments = $"--json={gpuCount} --output data{gpuCount}.json";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                string output;
                try
                {
                    process.Start();
                    output = process.StandardOutput.ReadToEnd();
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    MessageBox.Show("The vulkaninfo check failed. This usually means your GPU does not support Vulkan. Make sure your drivers are up-to-date - don't rely on Windows Update drivers, either. DXVK is not available.");
                    Logger.Error($"Running vulkaninfo on GPU{gpuCount} failed! User likely has outdated drivers or an extremely old GPU.");
                    return (0, 0, false, false, false, false);
                }

                process.WaitForExit(10);
                if (process.ExitCode != 0 && output.Contains("The selected gpu"))
                {
                    Logger.Debug($"GPU{gpuCount} doesn't exist, moving on");
                    break;
                }
                else if (!File.Exists($"data{gpuCount}.json"))
                {
                    Logger.Debug($"Failed to run vulkaninfo via the first method, trying again...");
                    process.StartInfo.Arguments = $"--json={gpuCount} --output data{gpuCount}.json > data{gpuCount}.json";
                    process.Start();
                    process.WaitForExit(10);
                    if (process.ExitCode != 0 && output.Contains("The selected gpu"))
                    {
                        Logger.Debug($"GPU{gpuCount} doesn't exist, moving on");
                        break;
                    }
                    else if (!File.Exists($"data{gpuCount}.json"))
                    {
                        MessageBox.Show("The vulkaninfo check failed. This usually means your GPU does not support Vulkan. Make sure your drivers are up-to-date - don't rely on Windows Update drivers, either. DXVK is not available.");
                        Logger.Error($"Running vulkaninfo on GPU{gpuCount} failed! User likely has outdated drivers or an extremely old GPU.");
                        return (0, 0, false, false, false, false);
                    }
                }

                gpuCount++;
            }

            Logger.Debug($"Analyzing the vulkaninfo for every .json generated...");
            for (int gpuIndex = 0; gpuIndex < gpuCount; gpuIndex++)
            {
                Logger.Debug($"Checking data{gpuIndex}.json...");
                if (File.Exists($"data{gpuIndex}.json"))
                {
                    using (StreamReader file = File.OpenText($"data{gpuIndex}.json"))
                    {
                        int dxvkSupport = 0;
                        JsonDocument doc;
                        try
                        {
                            doc = JsonDocument.Parse(file.ReadToEnd());
                        }
                        catch (JsonException)
                        {
                            Logger.Error($"Failed to read data{gpuIndex}.json. Setting default values assuming the user has no Vulkan 1.1+ support.");
                            MessageBox.Show("Failed to read the json. Make sure your drivers are up-to-date - don't rely on Windows Update drivers, either.\n\nThe app will proceed assuming you have no support for DXVK, but that may not be the case.");
                            return (0, 0, false, false, false, false);
                        }

                        JsonElement root = doc.RootElement;
                        if (root.TryGetProperty("capabilities", out JsonElement capabilities))
                        {
                            JsonElement deviceProperties = capabilities.GetProperty("device").GetProperty("properties");
                            string deviceName = deviceProperties.GetProperty("VkPhysicalDeviceProperties").GetProperty("deviceName").GetString();
                            uint apiVersion = deviceProperties.GetProperty("VkPhysicalDeviceProperties").GetProperty("apiVersion").GetUInt32();
                            (int, int) vulkanVer = ConvertApiVersion(apiVersion);
                            int vulkanVerMajor = vulkanVer.Item1;
                            int vulkanVerMinor = vulkanVer.Item2;

                            Logger.Info($"{deviceName}'s supported Vulkan version is: {vulkanVerMajor}.{vulkanVerMinor}");

                            if (deviceName.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0)

                            {
                                Logger.Info($"GPU{gpuIndex} is an NVIDIA GPU.");
                                nvidiaGpu = true;
                            }

                            try
                            {
                                Logger.Debug($"Checking if GPU{gpuIndex} supports DXVK 2.x...");
                                if (capabilities.GetProperty("extensions").TryGetProperty("VK_EXT_robustness2", out _)
                                    && capabilities.GetProperty("extensions").TryGetProperty("VK_EXT_transform_feedback", out _)
                                    && capabilities.GetProperty("features").GetProperty("VkPhysicalDeviceRobustness2FeaturesEXT").GetProperty("robustBufferAccess2").GetBoolean()
                                    && capabilities.GetProperty("features").GetProperty("VkPhysicalDeviceRobustness2FeaturesEXT").GetProperty("nullDescriptor").GetBoolean())
                                {
                                    Logger.Info($"GPU{gpuIndex} supports DXVK 2.x, yay!");
                                    dxvkSupport = 2;
                                }
                                else
                                {
                                    Logger.Debug($"GPU{gpuIndex} doesn't support DXVK 2.x, throwing an exception because doing it any other way is annoying...");
                                    throw new System.Exception();
                                }
                            }
                            catch
                            {
                                Logger.Debug($"Catched an exception, this means GPU{gpuIndex} doesn't support DXVK 2.x, checking other versions...");
                                if (vulkanVerMajor <= 1 && vulkanVerMinor == 1)
                                {
                                    Logger.Info($"GPU{gpuIndex} doesn't support DXVK or has outdated drivers.");
                                }
                                else if (vulkanVerMajor == 1 && vulkanVerMinor < 3)
                                {
                                    Logger.Info($"GPU{gpuIndex} supports Legacy DXVK 1.x.");
                                    dxvkSupport = 1;
                                }
                            }

                            if (deviceProperties.GetProperty("VkPhysicalDeviceProperties").GetProperty("deviceType").GetString() == "VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU" && dxvkSupport > dgpuDxvkSupport)
                            {
                                Logger.Info($"GPU{gpuIndex} is a discrete GPU.");
                                dgpuDxvkSupport = dxvkSupport;
                                igpuOnly = false;
                            }
                            else if (dxvkSupport > igpuDxvkSupport)
                            {
                                Logger.Info($"GPU{gpuIndex} is an integrated GPU.");
                                igpuDxvkSupport = dxvkSupport;
                                dgpuOnly = false;

                                if (deviceName.Contains("Intel"))
                                {
                                    Logger.Info($"GPU{gpuIndex} is an integrated Intel iGPU.");
                                    intelIgpu = true;
                                }
                            }
                        }
                        else if (root.TryGetProperty("VkPhysicalDeviceProperties", out JsonElement deviceProperties))
                        {
                            Logger.Debug($"Couldn't check the json normally, user likely has an Intel iGPU. Performing alternative check...");
                            JsonElement deviceName = deviceProperties.GetProperty("deviceName");
                            JsonElement vulkanVer = root.GetProperty("comments").GetProperty("vulkanApiVersion");

                            Logger.Info($"{deviceName}'s supported Vulkan version is: {vulkanVer}");

                            if (deviceName.ToString().Contains("HD Graphics"))
                            {
                                Logger.Info($"GPU{gpuIndex} is an integrated Intel iGPU.");
                                dgpuOnly = false;
                                intelIgpu = true;
                            }

                            if (System.Convert.ToInt16(vulkanVer.ToString().Split('.')[0]) >= 1 && System.Convert.ToInt16(vulkanVer.ToString().Split('.')[1]) >= 1)
                            {
                                Logger.Info($"GPU{gpuIndex} supports Legacy DXVK 1.x.");
                                igpuDxvkSupport = 1;
                            }
                        }
                        else
                        {
                            Logger.Error($"Failed to read data{gpuIndex}.json. Setting default values assuming the user has an Intel iGPU.");
                            MessageBox.Show("Failed to read the json. Make sure your drivers are up-to-date - don't rely on Windows Update drivers, either.\n\nThe app will proceed assuming you have an Intel iGPU with outdated drivers, but that may not be the case.");
                            igpuOnly = true;
                            dgpuOnly = false;
                            intelIgpu = true;
                            igpuDxvkSupport = 1;
                        }
                    }

                    Logger.Debug($"Removing data{gpuIndex}.json...");
                    File.Delete($"data{gpuIndex}.json");
                }
                else
                {
                    break;
                }
            }

            return (dgpuDxvkSupport, igpuDxvkSupport, igpuOnly, dgpuOnly, intelIgpu, nvidiaGpu);
        }
    }
}

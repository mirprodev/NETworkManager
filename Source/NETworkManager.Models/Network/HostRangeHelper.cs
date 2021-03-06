using NETworkManager.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NETworkManager.Models.Network
{
    public static class HostRangeHelper
    {
        public static Task<IPAddress[]> CreateIPAddressesFromIPRangesAsync(string[] ipRanges, CancellationToken cancellationToken)
        {
            return Task.Run(() => CreateIPAddressesFromIPRanges(ipRanges, cancellationToken), cancellationToken);
        }

        public static IPAddress[] CreateIPAddressesFromIPRanges(string[] ipRanges, CancellationToken cancellationToken)
        {
            var bag = new ConcurrentBag<IPAddress>();

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken
            };

            foreach (var ipOrRange in ipRanges)
            {
                // 192.168.0.1
                if (Regex.IsMatch(ipOrRange, RegexHelper.IPv4AddressRegex))
                {
                    bag.Add(IPAddress.Parse(ipOrRange));
                    continue;
                }

                // 192.168.0.0/24 or 192.168.0.0/255.255.255.0
                if (Regex.IsMatch(ipOrRange, RegexHelper.IPv4AddressCidrRegex) || Regex.IsMatch(ipOrRange, RegexHelper.IPv4AddressSubnetmaskRegex))
                {
                    var network = IPNetwork.Parse(ipOrRange);

                    Parallel.For(IPv4Address.ToInt32(network.Network), IPv4Address.ToInt32(network.Broadcast) + 1, parallelOptions, i =>
                    {
                        bag.Add(IPv4Address.FromInt32(i));

                        parallelOptions.CancellationToken.ThrowIfCancellationRequested();
                    });

                    continue;
                }

                // 192.168.0.0 - 192.168.0.100
                if (Regex.IsMatch(ipOrRange, RegexHelper.IPv4AddressRangeRegex))
                {
                    var range = ipOrRange.Split('-');

                    Parallel.For(IPv4Address.ToInt32(IPAddress.Parse(range[0])), IPv4Address.ToInt32(IPAddress.Parse(range[1])) + 1, parallelOptions, i =>
                    {
                        bag.Add(IPv4Address.FromInt32(i));

                        parallelOptions.CancellationToken.ThrowIfCancellationRequested();
                    });

                    continue;
                }

                // 192.168.[50-100,200].1 --> 192.168.50.1, 192.168.51.1, 192.168.52.1, {..}, 192.168.200.1
                if (Regex.IsMatch(ipOrRange, RegexHelper.IPv4AddressSpecialRangeRegex))
                {
                    var octets = ipOrRange.Split('.');

                    var list = new List<List<int>>();

                    // Go through each octet...
                    foreach (var octet in octets)
                    {
                        var innerList = new List<int>();

                        // Create a range for each octet
                        if (Regex.IsMatch(octet, RegexHelper.SpecialRangeRegex))
                        {
                            foreach (var numberOrRange in octet.Substring(1, octet.Length - 2).Split(','))
                            {
                                // 50-100
                                if (numberOrRange.Contains("-"))
                                {
                                    var rangeNumbers = numberOrRange.Split('-');

                                    for (var i = int.Parse(rangeNumbers[0]); i < (int.Parse(rangeNumbers[1]) + 1); i++)
                                    {
                                        innerList.Add(i);
                                    }
                                } // 200
                                else
                                {
                                    innerList.Add(int.Parse(numberOrRange));
                                }
                            }
                        }
                        else
                        {
                            innerList.Add(int.Parse(octet));
                        }

                        list.Add(innerList);
                    }

                    // Build the new ipv4
                    foreach (var i in list[0])
                    {
                        foreach (var j in list[1])
                        {
                            foreach (var k in list[2])
                            {
                                foreach (var h in list[3])
                                {
                                    bag.Add(IPAddress.Parse($"{i}.{j}.{k}.{h}"));
                                }
                            }
                        }
                    }

                    continue;
                }

                // 2001:db8:85a3::8a2e:370:7334
                if (Regex.IsMatch(ipOrRange, RegexHelper.IPv6AddressRegex))
                {
                    bag.Add(IPAddress.Parse(ipOrRange));
                }
            }

            return bag.ToArray();
        }

        public static Task<List<string>> ResolveHostnamesInIPRangesAsync(string[] ipRanges, CancellationToken cancellationToken)
        {
            return Task.Run(() => ResolveHostnamesInIPRanges(ipRanges, cancellationToken), cancellationToken);
        }

        public static List<string> ResolveHostnamesInIPRanges(string[] ipRanges, CancellationToken cancellationToken)
        {
            var bag = new ConcurrentBag<string>();

            var exceptions = new ConcurrentQueue<HostNotFoundException>();

            Parallel.ForEach(ipRanges, new ParallelOptions { CancellationToken = cancellationToken }, ipHostOrRange =>
            {
                // 192.168.0.1
                if (Regex.IsMatch(ipHostOrRange, RegexHelper.IPv4AddressRegex))
                {
                    bag.Add(ipHostOrRange);
                }
                // 192.168.0.0/24
                else if (Regex.IsMatch(ipHostOrRange, RegexHelper.IPv4AddressCidrRegex))
                {
                    bag.Add(ipHostOrRange);
                }
                // 192.168.0.0/255.255.255.0
                else if (Regex.IsMatch(ipHostOrRange, RegexHelper.IPv4AddressSubnetmaskRegex))
                {
                    bag.Add(ipHostOrRange);
                }
                // 192.168.0.0 - 192.168.0.100
                else if (Regex.IsMatch(ipHostOrRange, RegexHelper.IPv4AddressRangeRegex))
                {
                    bag.Add(ipHostOrRange);
                }
                // 192.168.[50-100].1
                else if (Regex.IsMatch(ipHostOrRange, RegexHelper.IPv4AddressSpecialRangeRegex))
                {
                    bag.Add(ipHostOrRange);
                }
                // 2001:db8:85a3::8a2e:370:7334
                else if (Regex.IsMatch(ipHostOrRange, RegexHelper.IPv6AddressRegex))
                {
                    bag.Add(ipHostOrRange);
                }
                // example.com
                else if(Regex.IsMatch(ipHostOrRange, RegexHelper.HostnameRegex))
                {
                    // Wait for task inside a Parallel.Foreach
                    var dnsResovlerTask = DnsLookupHelper.ResolveIPAddress(ipHostOrRange);

                    Task.WaitAll(dnsResovlerTask);

                    bag.Add($"{dnsResovlerTask.Result}");
                }
                // example.com/24 or example.com/255.255.255.128
                else if (Regex.IsMatch(ipHostOrRange, RegexHelper.HostnameCidrRegex) || Regex.IsMatch(ipHostOrRange, RegexHelper.HostnameSubnetmaskRegex))
                {
                    var hostAndSubnet = ipHostOrRange.Split('/');

                    // Wait for task inside a Parallel.Foreach
                    var dnsResovlerTask = DnsLookupHelper.ResolveIPAddress(hostAndSubnet[0]);

                    Task.WaitAll(dnsResovlerTask);

                    // IPv6 is not supported for ranges
                    if (dnsResovlerTask.Result == null || dnsResovlerTask.Result.AddressFamily != AddressFamily.InterNetwork)
                    {
                        exceptions.Enqueue(new HostNotFoundException(hostAndSubnet[0]));
                        return;
                    }

                    bag.Add($"{dnsResovlerTask.Result}/{hostAndSubnet[1]}");
                }
            });

            if (exceptions.Count > 0)
                throw new AggregateException(exceptions);

            return bag.ToList();
        }
    }
}
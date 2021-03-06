﻿using Google.Protobuf;
using ProfileServerCrypto;
using ProfileServerProtocol;
using Iop.Profileserver;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ProfileServerProtocolTests.Tests
{
  /// <summary>
  /// PS08013 - Neighborhood Initialization Process - Bad Role Server Neighbor Port
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08013---neighborhood-initialization-process---bad-role-server-neighbor-port
  /// </summary>
  public class PS08013 : ProtocolTest
  {
    public const string TestName = "PS08013";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
      new ProtocolTestArgument("Base Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


    /// <summary>Generated test profiles mapped by their name.</summary>
    public static Dictionary<string, ProtocolClient> TestProfiles = new Dictionary<string, ProtocolClient>(StringComparer.Ordinal);

    /// <summary>Random number generator.</summary>
    public static Random Rng = new Random();


    /// <summary>
    /// Implementation of the test itself.
    /// </summary>
    /// <returns>true if the test passes, false otherwise.</returns>
    public override async Task<bool> RunAsync()
    {
      IPAddress ServerIp = (IPAddress)ArgumentValues["Server IP"];
      int PrimaryPort = (int)ArgumentValues["primary Port"];
      int BasePort = (int)ArgumentValues["Base Port"];
      log.Trace("(ServerIp:'{0}',PrimaryPort:{1},BasePort:{2})", ServerIp, PrimaryPort, BasePort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      ProfileServer profileServer = null;
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        log.Trace("Step 1");
        // Get port list.
        await client.ConnectAsync(ServerIp, PrimaryPort, false);
        Dictionary<ServerRoleType, uint> rolePorts = new Dictionary<ServerRoleType, uint>();
        bool listPortsOk = await client.ListServerPorts(rolePorts);
        client.CloseConnection();


        bool profileInitializationOk = true;
        byte[] testImageData = File.ReadAllBytes(Path.Combine("images", TestName + ".jpg"));

        int profileIndex = 1;
        int profileCount = 10;
        for (int i = 0; i < profileCount; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          profileClient.InitializeRandomProfile(profileIndex, testImageData);
          profileIndex++;

          if (!await profileClient.RegisterAndInitializeProfileAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            profileClient.Dispose();
            profileInitializationOk = false;
            break;
          }

          TestProfiles.Add(profileClient.Profile.Name, profileClient);
        }

        profileServer = new ProfileServer("TestServer", ServerIp, BasePort, client.GetIdentityKeys());
        bool serverStartOk = profileServer.Start();

        bool step1Ok = listPortsOk && profileInitializationOk && serverStartOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");
        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        bool neighborhoodInitializationProcessOk = await client.NeighborhoodInitializationProcessAsync(profileServer.PrimaryPort, profileServer.ClientNonCustomerPort, TestProfiles);

        bool step2Ok = neighborhoodInitializationProcessOk;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");


        // Step 3
        log.Trace("Step 3");

        profileInitializationOk = true;
        profileCount = 5;
        Dictionary<string, ProtocolClient> newProfiles = new Dictionary<string, ProtocolClient>(StringComparer.Ordinal);
        for (int i = 0; i < profileCount; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          profileClient.InitializeRandomProfile(profileIndex, testImageData);
          profileIndex++;

          if (!await profileClient.RegisterAndInitializeProfileAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            profileClient.Dispose();
            profileInitializationOk = false;
            break;
          }

          TestProfiles.Add(profileClient.Profile.Name, profileClient);
          newProfiles.Add(profileClient.Profile.Name, profileClient);
        }

        // Wait at most 12 minutes for updates from the server.
        List<SharedProfileAddItem> addUpdates = new List<SharedProfileAddItem>();
        bool profilesOk = false;
        for (int time = 0; time < 12 * 60; time++) 
        {
          await Task.Delay(1000);

          // Meanwhile we expect updates to arrive on our simulated profile server.
          bool error = false;
          List<IncomingServerMessage> psMessages = profileServer.GetMessageList();
          if (psMessages.Count == 0) continue;

          foreach (IncomingServerMessage ism in psMessages)
          {
            if (ism.Role != ServerRole.ServerNeighbor) continue;
            Message message = ism.IncomingMessage;

            if ((message.MessageTypeCase == Message.MessageTypeOneofCase.Request)
              && (message.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest)
              && (message.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate))
            {
              foreach (SharedProfileUpdateItem updateItem in message.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items)
              {
                if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Add)
                {
                  SharedProfileAddItem addItem = updateItem.Add;
                  addUpdates.Add(addItem);
                }
                else
                {
                  log.Trace("Received invalid update action type {0}.", updateItem.ActionTypeCase);
                  error = true;
                  break;
                }
              }
            }

            if (error) break;
          }
          // Terminate if any error occurred.
          if (error) break;

          // Terminate if the received profiles match what is expected.
          profilesOk = client.CheckProfileListMatchAddItems(newProfiles, addUpdates);
          if (profilesOk) break;

          // Terminate if we do not expect any more updates to come.
          if (addUpdates.Count >= newProfiles.Count) break;
        }

        bool step3Ok = profileInitializationOk && profilesOk;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok && step3Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      client.Dispose();

      foreach (ProtocolClient protocolClient in TestProfiles.Values)
        protocolClient.Dispose();

      if (profileServer != null) profileServer.Shutdown();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}

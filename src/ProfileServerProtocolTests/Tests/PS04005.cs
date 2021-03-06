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
  /// PS04005 - Cancel Hosting Agreement, Register Again and Check-In
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS04.md#ps04005---cancel-hosting-agreement-register-again-and-check-in
  /// </summary>
  public class PS04005 : ProtocolTest
  {
    public const string TestName = "PS04005";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("clNonCustomer Port", ProtocolTestArgumentType.Port),
      new ProtocolTestArgument("clCustomer Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


    /// <summary>
    /// Implementation of the test itself.
    /// </summary>
    /// <returns>true if the test passes, false otherwise.</returns>
    public override async Task<bool> RunAsync()
    {
      IPAddress ServerIp = (IPAddress)ArgumentValues["Server IP"];
      int ClNonCustomerPort = (int)ArgumentValues["clNonCustomer Port"];
      int ClCustomerPort = (int)ArgumentValues["clCustomer Port"];
      log.Trace("(ServerIp:'{0}',ClNonCustomerPort:{1},ClCustomerPort:{2})", ServerIp, ClNonCustomerPort, ClCustomerPort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        await client.ConnectAsync(ServerIp, ClNonCustomerPort, true);
        bool establishHostingOk = await client.EstablishHostingAsync();

        // Step 1 Acceptance
        bool step1Ok = establishHostingOk;
        client.CloseConnection();


        // Step 2
        await client.ConnectAsync(ServerIp, ClCustomerPort, true);
        bool checkInOk = await client.CheckInAsync();

        Message requestMessage = mb.CreateCancelHostingAgreementRequest(null);
        await client.SendMessageAsync(requestMessage);
        Message responseMessage = await client.ReceiveMessageAsync();

        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;

        bool cancelHostingAgreementOk = idOk && statusOk;

        // Step 2 Acceptance
        bool step2Ok = checkInOk && cancelHostingAgreementOk;

        client.CloseConnection();

        // Step 3
        await client.ConnectAsync(ServerIp, ClCustomerPort, true);
        bool startConversationOk = await client.StartConversationAsync();

        requestMessage = mb.CreateCheckInRequest(client.Challenge);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorNotFound;
        checkInOk = idOk && statusOk;

        // Step 3 Acceptance
        bool step3Ok = startConversationOk && checkInOk;

        client.CloseConnection();



        // Step 4
        await client.ConnectAsync(ServerIp, ClNonCustomerPort, true);
        establishHostingOk = await client.EstablishHostingAsync();

        // Step 4 Acceptance
        bool step4Ok = establishHostingOk;
        client.CloseConnection();



        // Step 5
        await client.ConnectAsync(ServerIp, ClCustomerPort, true);
        checkInOk = await client.CheckInAsync();

        // Step 5 Acceptance
        bool step5Ok = checkInOk;


        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      client.Dispose();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}

﻿using System;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SharedCode.Model;
using WebServer.Model;
using WebServer.Repository;
using WebServer.Repository.Interface;

namespace WebServer.Service
{
    public class AccountService
    {
        private readonly ILogger<AccountService> _logger;
        private readonly IAccountRepository _accountRepository;
        private readonly IRedisRepository _redisRepository;
        private readonly DataManager _dataManager;

        public AccountService(ILogger<AccountService> logger, IAccountRepository accountRepository, IRedisRepository redisRepository, DataManager dataManager)
        {
            _logger = logger;
            _accountRepository = accountRepository;
            _redisRepository = redisRepository;
            _dataManager = dataManager;
        }

        public async Task<(bool, string)> CreateAsync(string id, string pw, string nickName)
        {
            if (await _accountRepository.IsAlreadyExistAsync(id))
            {
                string message = _dataManager.MessageHandler.GetMessage("Error", 2); // ID가 중복되었습니다.
                return (false, message);
            }
            if (pw.Length < 10)
            {
                string message = _dataManager.MessageHandler.GetMessage("Error", 3); // 비밀번호 10자 이상 만들어 주세요.
                return (false, message);
            }

            if (!Regex.IsMatch(pw, @"[a-zA-Z]") || !Regex.IsMatch(pw, @"\d") || !Regex.IsMatch(pw, @"[!@#$%^&*(),.?:{}|<>]"))
            {
                string message = _dataManager.MessageHandler.GetMessage("Error", 4); // 특수문자, 숫자, 영어가 혼합되어야 합니다.
                return (false, message);
            }

            // 비밀번호 해싱
            string hashedPw = Utils.HashPassword(pw);

            if (await _accountRepository.CreateAccountAsync(id, hashedPw, nickName))
            {
                return (true, _dataManager.MessageHandler.GetMessage("Info", 0));
            }
            else
            {
                string message = _dataManager.MessageHandler.GetMessage("Error", 6); // 계정 생성에 실패했습니다.
                return (false, message);
            }
        }

        public async Task<(bool, string, string)> LoginAsync(string id, string pw)
        {
            var account = await _accountRepository.GetAccountAsync(id);
            if (account == null)
            {
                string message = _dataManager.MessageHandler.GetMessage("Error", 4); // ID가 존재하지 않습니다.
                return (false, message, null);
            }

            if (!Utils.VerifyPassword(pw, account.UserPassword))
            {
                string message = _dataManager.MessageHandler.GetMessage("Error", 0); // 올바르지 않은 비밀번호입니다.
                return (false, message, null);
            }

            // 닉네임 가져오기 (null 체크 추가)
            var nickName = await _accountRepository.GetNickName(account.AccountId);
            if (nickName == null)
            {
                string message = _dataManager.MessageHandler.GetMessage("Error", 4); // 닉네임이 없습니다.
                return (false, message, null);
            }

            // 길드 ID 가져오기 (null 또는 유효성 검사 추가)
            var guildId = await _accountRepository.GetAccountGuildId(account.AccountId);
            if (!guildId.Item1 || guildId.Item2 == 0) // Item1이 false이거나, 길드가 없는 경우
            {
                string message = _dataManager.MessageHandler.GetMessage("Error", 4); // 길드가 존재하지 않습니다.
                return (false, message, null);
            }

            // 기존 세션 무효화
            await _redisRepository.DeleteSessionAsync(account.AccountId);

            // 새로운 세션 생성
            var sessionId = Guid.NewGuid().ToString();
            var sessionExpiration = TimeSpan.FromHours(1); // 세션 유효 기간 설정 (예: 1시간)

            var sessionCreated = await _redisRepository.SetSessionAsync(account.AccountId, sessionId, sessionExpiration);

            if (!sessionCreated)
            {
                string message = _dataManager.MessageHandler.GetMessage("Error", 7); // 세션 생성에 실패했습니다.
                return (false, message, null);
            }

            // UserEntity 생성 (null 값이 없도록 주의)
            UserEntity userEntity = new UserEntity
            {
                Userid = id,
                UserPW = pw,
                UserUID = account.AccountId,
                SessionId = sessionId,
                UserName = nickName.AccountNickName,  // 닉네임이 유효한지 확인
                GuildUID = guildId.Item2  // 길드 ID가 유효한지 확인
            };

            string jsonUserEntity = JsonConvert.SerializeObject(userEntity);

            return (true, _dataManager.MessageHandler.GetMessage("Info", 1), jsonUserEntity);
        }
        public async Task<(bool, string)> LogoutAsync(long accountId)
        {
            var isSuccess = await _redisRepository.DeleteSessionAsync(accountId);

            if (isSuccess)
            {
                return (true, "Logout Success");
            }
            else
            {
                return (false, "Logout Fail");
            }
        }


        public async Task<(bool, string)> ModifyNickNameAsync(long accountid, string nickName)
        {
            //Validation Check
            //if (nickName == null)
            //{
            //    string message = "Test"; // ID가 존재하지 않습니다.
            //    return (false, message);
            //}

            var isSuccess = await _accountRepository.ModifyNickName(accountid, nickName);

            if (isSuccess)
            {
                return (isSuccess, _dataManager.MessageHandler.GetMessage("Info", 2));
            }
            else
            {
                return (isSuccess, _dataManager.MessageHandler.GetMessage("Error", 9));
            }

        }
    }
}

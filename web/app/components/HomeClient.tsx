'use client'
import { useState } from 'react';
import { Button, Typography, Layout, Space, Input, Empty, Card, Row, Col, Statistic, Tooltip, Pagination, message, ConfigProvider } from 'antd';
import {
  PlusOutlined,
  DatabaseOutlined,
  GithubOutlined,
} from '@ant-design/icons';
import RepositoryForm from './RepositoryForm';
import RepositoryList from './RepositoryList';
import LastRepoModal from './LastRepoModal';
import { Repository, RepositoryFormValues } from '../types';
import { submitWarehouse } from '../services/warehouseService';
import { unstableSetRender } from 'antd';
import { createRoot } from 'react-dom/client';
import { homepage } from '../const/urlconst';

const { Content, Header, Footer } = Layout;
const { Title, Paragraph, Text } = Typography;
const { Search } = Input;

// 自定义主题配置
const customTheme = {
  token: {
    colorPrimary: '#3f51b5',
    colorSuccess: '#4caf50',
    colorError: '#f44336',
    colorWarning: '#ff9800',
    colorBgContainer: '#ffffff',
    colorBgLayout: '#f7f9fc',
    colorText: '#333333',
    colorTextSecondary: '#666666',
    borderRadius: 4,
    fontSizeHeading2: 26,
    fontSizeHeading3: 20,
    fontSizeBase: 14,
    boxShadow: '0 2px 8px rgba(0, 0, 0, 0.08)',
  },
  components: {
    Button: {
      colorPrimary: '#3f51b5',
      algorithm: true,
      borderRadius: 4,
      controlHeight: 40,
    },
    Card: {
      boxShadow: '0 2px 8px rgba(0, 0, 0, 0.08)',
      borderRadius: 8,
    },
    Input: {
      borderRadius: 4,
    },
    Message: {
      duration: 0.1,
    },
    Modal: {
    },
    Dropdown: {
      motion: false,
    },
    Pagination: {
      motion: false,
    },
    Tooltip: {
      motion: false,
    },
  },
};

unstableSetRender((node, container) => {
  // @ts-ignore
  container._reactRoot ||= createRoot(container);
  // @ts-ignore
  const root = container._reactRoot;
  root.render(node);
  return async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
    root.unmount();
  };
});

interface HomeClientProps {
  initialRepositories: Repository[];
  initialTotal: number;
  initialPage: number;
  initialPageSize: number;
  initialSearchValue: string;
}

export default function HomeClient({ initialRepositories, initialTotal, initialPage, initialPageSize, initialSearchValue }: HomeClientProps) {
  const [repositories, setRepositories] = useState<Repository[]>(initialRepositories);
  const [formVisible, setFormVisible] = useState(false);
  const [lastRepoModalVisible, setLastRepoModalVisible] = useState(false);
  const [searchValue, setSearchValue] = useState<string>(initialSearchValue);
  const [currentPage, setCurrentPage] = useState(initialPage);
  const [pageSize, setPageSize] = useState(initialPageSize);
  const [logoRotate, setLogoRotate] = useState<'none' | 'cw' | 'ccw'>('none');
  const [underlineWidth, setUnderlineWidth] = useState(0);

  const handleAddRepository = async (values: RepositoryFormValues) => {
    try {
      const response = await submitWarehouse(values);
      if (response.success) {
        message.config({
          duration: 1.5,
        });
        message.success('仓库添加成功');
        // 刷新页面以获取最新数据
        window.location.reload();
      } else {
        message.config({
          duration: 1.5,
        });
        message.error('添加仓库失败: ' + (response.error || '未知错误'));
      }
    } catch (error) {
      console.error('添加仓库出错:', error);
      message.config({
        duration: 1.5,
      });
      message.error('添加仓库出错，请稍后重试');
    }
    setFormVisible(false);
  };

  const handleLastRepoQuery = () => {
    setLastRepoModalVisible(true);
  };

  const handlePageChange = (page: number, size?: number) => {
    setCurrentPage(page);
    if (size) setPageSize(size);
    window.location.href = `/?page=${page}&pageSize=${size || pageSize}&keyword=${searchValue}`;
  };

  const handleSearch = (value: string) => {
    setSearchValue(value);
    setCurrentPage(1);
    setPageSize(initialPageSize);
    window.location.href = `/?page=${1}&pageSize=${initialPageSize}&keyword=${value}`;
  };

  // 计算统计数据
  const stats = {
    totalRepositories: initialTotal || repositories.length,
    gitRepos: repositories.filter(repo => repo.type === 'git').length,
    lastUpdated: repositories.length ? new Date(
      Math.max(...repositories.map(repo => new Date(repo.createdAt).getTime()))
    ).toLocaleDateString('zh-CN') : '-'
  };

  const contentStyle = {
    padding: '24px',
    background: customTheme.token.colorBgLayout
  };

  const pageContainerStyle = {
    maxWidth: 1200,
    margin: '0 auto'
  };

  const cardStyle = {
    borderRadius: 8,
    marginBottom: 24,
    boxShadow: customTheme.token.boxShadow,
    border: 'none'
  };

  const welcomeTitleStyle = {
    fontSize: '2rem',
    fontWeight: 600,
    marginBottom: 8,
    color: customTheme.token.colorText
  };

  const paragraphStyle = {
    fontSize: 15,
    marginBottom: 24,
    color: customTheme.token.colorTextSecondary
  };

  const buttonStyle = {
    height: 40,
    fontWeight: 500,
    boxShadow: 'none'
  };

  const primaryButtonStyle = {
    ...buttonStyle,
    background: customTheme.token.colorPrimary
  };

  const searchStyle = {
    width: 300,
    borderRadius: 4
  };

  const pageHeaderStyle = {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    margin: '24px 0 16px',
    flexWrap: 'wrap' as const,
    gap: '16px'
  };

  const statisticStyle = {
    padding: '8px 12px',
    background: 'rgba(63, 81, 181, 0.05)',
    borderRadius: 6
  };

  return (
    <ConfigProvider theme={customTheme as any}>
      <Layout style={{ minHeight: '100vh' }}>
        <Header
          style={{
            background: customTheme.token.colorPrimary,
            padding: '0 24px',
            position: 'sticky',
            top: 0,
            zIndex: 1000,
            boxShadow: '0 2px 8px rgba(0, 0, 0, 0.15)'
          }}
        >
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              maxWidth: 1200,
              margin: '0 auto',
              width: '100%',
              height: '100%'
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center' }}>
              <Typography.Title level={3} style={{
                color: 'white',
                margin: 0,
                fontSize: 22,
                letterSpacing: '0.5px',
                fontWeight: 600
              }}>
                OpenDeepWiki
              </Typography.Title>
              <span style={{
                color: 'white',
                fontSize: 13,
                fontWeight: 400,
                marginLeft: 10,
                opacity: 0.8
              }}>
                开源的DeepWiki，让您眼前一亮！
              </span>
            </div>

            <Space size={16} align="center">
              <div
                style={{
                  position: 'relative',
                  display: 'inline-block',
                  cursor: 'pointer'
                }}
                onMouseEnter={() => {
                  setLogoRotate('cw');
                  setUnderlineWidth(100);
                }}
                onMouseLeave={() => {
                  setLogoRotate('ccw');
                  setUnderlineWidth(0);
                }}
                onTransitionEnd={() => setLogoRotate('none')}
              >
                <span style={{
                  color: 'rgba(255, 255, 255, 0.8)',
                  fontSize: 14,
                  fontWeight: 400,
                  opacity: 0.8,
                  display: 'flex',
                  position: 'relative',
                  userSelect: 'none',
                  alignItems: 'center',
                  gap: '4px'
                }}>
                  powered by
                  <span style={{
                    position: 'relative',
                    color: 'white',
                    userSelect: 'none',
                    alignItems: 'center',
                    justifyContent: 'center',
                    display: 'flex',
                    fontWeight: 600,
                    gap: '4px'
                  }}>
                    OpenDeepWiki
                  </span>
                  <img
                    src="/logo.png"
                    alt="OpenDeepWiki"
                    style={{
                      width: 20,
                      height: 20,
                      transition: 'transform 0.6s cubic-bezier(0.4,0,0.2,1)',
                      transform:
                        logoRotate === 'cw'
                          ? 'rotate(360deg)'
                          : logoRotate === 'ccw'
                            ? 'rotate(-180deg)'
                            : 'rotate(0deg)'
                    }}
                  />
                </span>
              </div>
              <Tooltip title="源码地址" placement="bottom">
                <Button
                  type="text"
                  icon={<GithubOutlined style={{ fontSize: 18 }} />}
                  style={{
                    color: 'white',
                    height: 40,
                    width: 40,
                    borderRadius: 8,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    background: 'rgba(255, 255, 255, 0.1)',
                    transition: 'all 0.3s'
                  }}
                  href={homepage}
                  target="_blank"
                />
              </Tooltip>
            </Space>
          </div>
        </Header>

        <Content style={contentStyle}>
          <div style={pageContainerStyle}>
            <Row gutter={[24, 24]}>
              <Col span={24}>
                <Card style={cardStyle}>
                  <Row gutter={24} align="middle">
                    <Col xs={24} md={16}>
                      <div style={{ paddingRight: 24 }}>
                        <Title level={2} style={welcomeTitleStyle}>
                          AI驱动的代码知识库
                        </Title>
                        <Paragraph style={paragraphStyle}>
                          OpenDeepWiki 是参考DeepWiki作为灵感，基于 .NET 9 和 Semantic Kernel 开发的开源项目。它旨在帮助开发者更好地理解和使用代码库，提供代码分析、文档生成等功能。
                        </Paragraph>
                        <Space size={16}>
                          <Button
                            type="primary"
                            size="large"
                            icon={<PlusOutlined />}
                            onClick={() => setFormVisible(true)}
                            style={primaryButtonStyle}
                          >
                            添加新仓库
                          </Button>
                          <Button
                            type="default"
                            size="large"
                            onClick={handleLastRepoQuery}
                            style={buttonStyle}
                          >
                            查询上次提交仓库
                          </Button>
                        </Space>
                      </div>
                    </Col>
                    <Col xs={24} md={8}>
                      <Row gutter={[16, 16]}>
                        <Col span={12}>
                          <div style={statisticStyle}>
                            <Statistic
                              title={<Typography.Text style={{ color: customTheme.token.colorTextSecondary }}>仓库总数</Typography.Text>}
                              value={stats.totalRepositories}
                              valueStyle={{ color: customTheme.token.colorText, fontWeight: 600 }}
                              prefix={<DatabaseOutlined style={{ color: customTheme.token.colorPrimary }} />}
                            />
                          </div>
                        </Col>
                        <Col span={12}>
                          <div style={statisticStyle}>
                            <Statistic
                              title={<Typography.Text style={{ color: customTheme.token.colorTextSecondary }}>Git仓库</Typography.Text>}
                              value={stats.gitRepos}
                              valueStyle={{ color: customTheme.token.colorText, fontWeight: 600 }}
                              prefix={<GithubOutlined style={{ color: customTheme.token.colorPrimary }} />}
                            />
                          </div>
                        </Col>
                      </Row>
                    </Col>
                  </Row>
                </Card>
              </Col>
            </Row>

            <div style={pageHeaderStyle}>
              <Title level={3} style={{ margin: 0, fontSize: customTheme.token.fontSizeHeading3 }}>仓库列表</Title>
              <Space wrap>
                <Search
                  placeholder="搜索仓库名称或地址"
                  allowClear
                  value={searchValue}
                  onSearch={value => handleSearch(value)}
                  onChange={e => setSearchValue(e.target.value)}
                  style={searchStyle}
                />
                <Button
                  type="primary"
                  icon={<PlusOutlined />}
                  onClick={() => setFormVisible(true)}
                  style={primaryButtonStyle}
                >
                  添加仓库
                </Button>
              </Space>
            </div>

            {repositories.length === 0 ? (
              <Card style={{ ...cardStyle, padding: 32, textAlign: 'center' }}>
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description={
                    <Typography.Text style={{ color: customTheme.token.colorTextSecondary, fontSize: 15 }}>
                      {searchValue ? `没有找到与"${searchValue}"相关的仓库` : "暂无仓库数据"}
                    </Typography.Text>
                  }
                >
                  <Button type="primary" style={primaryButtonStyle} onClick={() => setFormVisible(true)}>
                    立即添加
                  </Button>
                </Empty>
              </Card>
            ) : (
              <>
                <RepositoryList repositories={repositories} />
                {!searchValue && initialTotal > pageSize && (
                  <div style={{ textAlign: 'center', marginTop: 24 }}>
                    <Pagination
                      current={currentPage}
                      pageSize={pageSize}
                      total={initialTotal}
                      onChange={handlePageChange}
                      showSizeChanger
                      showQuickJumper
                      showTotal={(total) => `共 ${total} 个仓库`}
                    />
                  </div>
                )}
              </>
            )}

            <RepositoryForm
              open={formVisible}
              onCancel={() => setFormVisible(false)}
              onSubmit={handleAddRepository}
            />

            <LastRepoModal
              open={lastRepoModalVisible}
              onCancel={() => setLastRepoModalVisible(false)}
            />
          </div>
        </Content>
        
        <Footer style={{
          textAlign: 'center',
          background: customTheme.token.colorBgContainer,
          padding: '12px 24px',
          marginTop: 'auto',
          borderTop: `1px solid #f0f0f0`
        }}>
          <Space direction="vertical" size={4}>
            <Text type="secondary" style={{ fontSize: customTheme.token.fontSizeBase - 2 }}>
              Powered by OpenDeepWiki
            </Text>
            <Text type="secondary" style={{ fontSize: customTheme.token.fontSizeBase - 2 }}>
              Powered by .NET 9.0
            </Text>
          </Space>
        </Footer>
      </Layout>
    </ConfigProvider>
  );
} 